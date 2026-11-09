using MySql.Data.MySqlClient;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MySqlRestoreFolder
{
    class Program
    {
        // Default: TaleteWebForms (ex talete.net). Fallback al vecchio nome per i rami non ancora rinominati.
        static readonly string ConfigRelativePath = File.Exists(@"TaleteWebForms\Web.config")
            ? @"TaleteWebForms\Web.config"
            : @"talete.net\Web.config";
        const string ConnectionStringName = "serverdedicato";
        const string Folder = "talete_development";

        static int Main(string[] args)
        {
            string folder = Path.GetFullPath(Folder);

            string connectionString;
            try
            {
                connectionString = ReadConnectionString(ConfigRelativePath, ConnectionStringName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to read connection string '{ConnectionStringName}' from '{ConfigRelativePath}': {ex.Message}");
                return 2;
            }

            // Force AllowUserVariables=true so MySQL @vars inside routines (e.g.
            // SET @index = ...) are not interpreted as ADO.NET parameters by
            // MySql.Data — otherwise CREATE PROCEDURE bodies fail with
            // "Parameter '@xxx' must be defined".
            var csb = new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true };
            connectionString = csb.ConnectionString;

            using (var conn = new MySqlConnection(connectionString))
            using (var cmd = new MySqlCommand())
            {
                conn.Open();
                cmd.Connection = conn;

                // Disable FK / unique checks for the whole restore session. The dump
                // headers (/*!40014 SET FOREIGN_KEY_CHECKS=0 */) are versioned-comment
                // statements that MySql.Data strips client-side before sending, so they
                // never reach the server. Without this, files are restored in
                // alphabetical order and FK targets created later (e.g. religioni after
                // pazienti) cause errno 150 on CREATE TABLE.
                using (var pre = new MySqlCommand(
                    "SET FOREIGN_KEY_CHECKS=0; SET UNIQUE_CHECKS=0; SET SQL_NOTES=0;",
                    conn))
                {
                    pre.ExecuteNonQuery();
                }

                var mb = new MySqlBackup(cmd);
                // Parallel import silently dropped routine errors (Phase2 re-throws but
                // the outer catch saw stopProcess=true and swallowed). Single-threaded
                // is safer for files containing DELIMITER blocks (procedures, functions,
                // triggers, views).
                mb.ImportInfo.EnableParallelProcessing = false;
                Console.WriteLine($"Restoring from folder '{folder}' into {conn.Database}...");

                var sqlFiles = Directory.GetFiles(folder, "*.sql");
                Array.Sort(sqlFiles, StringComparer.OrdinalIgnoreCase);
                Console.WriteLine($"Found {sqlFiles.Length} .sql files. Importing one by one...");

                int ok = 0, failed = 0;
                long totalBytes = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (string file in sqlFiles)
                {
                    try
                    {
                        mb.ImportFromFile(file);
                        ok++;
                        totalBytes += new FileInfo(file).Length;
                        if (ok <= 3 || ok % 200 == 0)
                            Console.WriteLine($"  [{ok}/{sqlFiles.Length}] {Path.GetFileName(file)} OK");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.Error.WriteLine($"  FAIL {Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
                        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                        {
                            Console.Error.WriteLine($"    -> inner: {inner.GetType().Name}: {inner.Message}");
                        }
                        if (mb.LastErrorSQL != null && mb.LastErrorSQL.Length > 0)
                        {
                            var snippet = mb.LastErrorSQL.Length > 400 ? mb.LastErrorSQL.Substring(0, 400) + "..." : mb.LastErrorSQL;
                            Console.Error.WriteLine($"    LastErrorSQL ({mb.LastErrorSQL.Length} bytes): {snippet}");
                        }
                        if (failed >= 5)
                        {
                            Console.Error.WriteLine("Too many failures, aborting.");
                            break;
                        }
                    }
                }
                sw.Stop();
                Console.WriteLine($"Done. ok={ok} failed={failed} bytes={totalBytes} elapsed={sw.Elapsed}");
            }

            return 0;
        }

        static string ReadConnectionString(string configRelativePath, string name)
        {
            string fullPath = Path.GetFullPath(configRelativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Web.config not found at '{fullPath}'.");

            var doc = XDocument.Load(fullPath);
            var entry = doc.Descendants("connectionStrings")
                           .Elements("add")
                           .FirstOrDefault(e => (string)e.Attribute("name") == name);

            if (entry == null)
                throw new InvalidOperationException($"Connection string '{name}' not found.");

            string cs = (string)entry.Attribute("connectionString");
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException($"Connection string '{name}' is empty.");

            return cs;
        }
    }
}
