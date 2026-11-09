using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MySql.Data.MySqlClient
{
    public partial class MySqlBackup
    {
        // File name prefixes are chosen so that case-insensitive lexicographic
        // ordering of *.sql files matches the required execution order for restore
        // (AA<db> header → <table>.sql tables → ZZ1..ZZ6 routines/views/triggers/footer).
        // ImportFromDirectory sorts using OrdinalIgnoreCase so that uppercase AA/ZZ
        // sit before/after the lowercase table file names.
        const string FolderHeaderFilePattern = "AA{0}.sql";
        const string FolderProceduresFile = "ZZ1procedures.sql";
        const string FolderFunctionsFile = "ZZ2functions.sql";
        const string FolderViewsFile = "ZZ3views.sql";
        const string FolderEventsFile = "ZZ4events.sql";
        const string FolderTriggersFile = "ZZ5triggers.sql";
        const string FolderFooterFile = "ZZ6footer.sql";

        /// <summary>
        /// Exports the database to a folder, splitting the dump into one .sql file
        /// per logical section (header, each table, routines, views, triggers, footer).
        /// The folder can be restored later with <see cref="ImportFromDirectory(string)"/>.
        /// </summary>
        public void ExportToFolder(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath))
                    throw new ArgumentNullException(nameof(folderPath), "Folder path cannot be null or empty.");

                Directory.CreateDirectory(folderPath);

                Export_InitializeVariables();

                string headerFileName = string.Format(FolderHeaderFilePattern, ExportFolder_SanitizeFileName(_database.Name));

                int stage = 1;
                Dictionary<string, string> tablesToExport = null;

                while (stage < 9)
                {
                    if (stopProcess) break;

                    switch (stage)
                    {
                        case 1:
                            ExportFolder_WriteSection(Path.Combine(folderPath, headerFileName), () =>
                            {
                                Export_BasicInfo();
                                Export_CreateDatabase();
                                Export_DocumentHeader();
                            });
                            break;
                        case 2:
                            tablesToExport = ExportFolder_TableRows(folderPath);
                            break;
                        case 3:
                            ExportFolder_WriteSection(Path.Combine(folderPath, FolderProceduresFile), Export_Procedures);
                            break;
                        case 4:
                            ExportFolder_WriteSection(Path.Combine(folderPath, FolderFunctionsFile), Export_Functions);
                            break;
                        case 5:
                            ExportFolder_WriteSection(Path.Combine(folderPath, FolderViewsFile), Export_Views);
                            break;
                        case 6:
                            ExportFolder_WriteSection(Path.Combine(folderPath, FolderEventsFile), Export_Events);
                            break;
                        case 7:
                            ExportFolder_WriteSection(Path.Combine(folderPath, FolderTriggersFile), Export_Triggers);
                            break;
                        case 8:
                            ExportFolder_WriteSection(Path.Combine(folderPath, FolderFooterFile), Export_DocumentFooter);
                            break;
                    }

                    stage++;
                }

                if (stopProcess) processCompletionType = ProcessEndType.Cancelled;
                else processCompletionType = ProcessEndType.Complete;

                Export_RaiseCompletionEvent();
            }
            catch (Exception ex)
            {
                _lastError = ex;
                StopAllProcess();
                Export_RaiseCompletionEvent();
                throw;
            }
            finally
            {
                ReportEndProcess();
                Export_RestoreOriginalVariables();
                textWriter = null;
            }
        }

        void ExportFolder_WriteSection(string filePath, Action exportSection)
        {
            if (stopProcess) return;

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, GetOptimalStreamBufferSize()))
            using (var writer = new StreamWriter(fs, textEncoding, GetOptimalStreamBufferSize()))
            {
                textWriter = writer;
                exportSection();
                textWriter.Flush();
            }

            textWriter = null;
        }

        Dictionary<string, string> ExportFolder_TableRows(string folderPath)
        {
            if (!ExportInfo.ExportTableStructure && !ExportInfo.ExportRows)
                return null;

            Dictionary<string, string> dicTables = Export_GetTablesToBeExportedReArranged();
            _totalTables = dicTables.Count;

            if (ExportProgressChanged != null)
                timerReport.Start();

            int tableIndex = 0;

            try
            {
                foreach (KeyValuePair<string, string> kvTable in dicTables)
                {
                    if (stopProcess)
                        return dicTables;

                    string tableName = kvTable.Key;
                    string selectSQL = kvTable.Value;

                    if (Export_ThisTableIsExcluded(tableName))
                        continue;

                    tableIndex++;
                    _currentTableName = tableName;
                    _currentTableIndex = tableIndex;
                    _totalRowsInCurrentTable = _database.Tables[tableName].TotalRows;

                    string fileName = ExportFolder_SanitizeFileName(tableName) + ".sql";
                    string filePath = Path.Combine(folderPath, fileName);

                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, GetOptimalStreamBufferSize()))
                    using (var writer = new StreamWriter(fs, textEncoding, GetOptimalStreamBufferSize()))
                    {
                        textWriter = writer;

                        if (ExportInfo.ExportTableStructure)
                            Export_TableStructure(tableName);

                        if (ExportInfo.ExportRows && !Export_ThisRowForTableIsExcluded(tableName))
                            Export_Rows(tableName, selectSQL);

                        textWriter.Flush();
                    }

                    textWriter = null;
                }
            }
            finally
            {
                timerReport.Stop();
            }

            return dicTables;
        }

        static string ExportFolder_SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Restores a database from a folder previously produced by
        /// <see cref="ExportToFolder(string)"/>. Files are imported in
        /// lexicographic order so that the naming scheme drives execution order.
        /// </summary>
        public void ImportFromDirectory(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                throw new ArgumentNullException(nameof(folderPath), "Folder path cannot be null or empty.");

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            string[] files = Directory.GetFiles(folderPath, "*.sql");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            // Note: do NOT check `stopProcess` between files. ImportFromFile's
            // finally calls ReportEndProcess → StopAllProcess, which sets the
            // flag to true on every successful file. Breaking on it here would
            // make the loop exit after the very first file. ImportFromFile
            // resets stopProcess to false at the start via Import_InitializeVariables,
            // so the next iteration always starts clean.
            foreach (string file in files)
            {
                ImportFromFile(file);
            }
        }
    }
}
