using MySql.Data.MySqlClient;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MySqlBackupFolder
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

            var csb = new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true };
            connectionString = csb.ConnectionString;

            using (var conn = new MySqlConnection(connectionString))
            using (var cmd = new MySqlCommand())
            {
                conn.Open();
                cmd.Connection = conn;

                int Count(string sql)
                {
                    cmd.CommandText = sql;
                    int n = 0;
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read()) n++;
                    }
                    return n;
                }
                Console.WriteLine($"DB inventory in '{conn.Database}':");
                Console.WriteLine($"  tables    = {Count("SHOW FULL TABLES WHERE Table_type = 'BASE TABLE';")}");
                Console.WriteLine($"  views     = {Count("SHOW FULL TABLES WHERE Table_type = 'VIEW';")}");
                Console.WriteLine($"  procedures= {Count($"SHOW PROCEDURE STATUS WHERE Db = '{conn.Database}';")}");
                Console.WriteLine($"  functions = {Count($"SHOW FUNCTION STATUS WHERE Db = '{conn.Database}';")}");
                Console.WriteLine($"  triggers  = {Count("SHOW TRIGGERS;")}");
                Console.WriteLine($"  events    = {Count($"SHOW EVENTS FROM `{conn.Database}`;")}");

                var mb = new MySqlBackup(cmd);
                // Drop TIME_ZONE statements from the dump headers/footers. The library
                // default sets the session to '+00:00' (UTC) before reading rows, which
                // shifts every TIMESTAMP column by the local UTC offset versus the
                // historical dumps (those were taken with session TZ = system). The
                // statements are removed from both the in-session execution (init phase)
                // and from the written file, so the new dump matches the old format.
                mb.ExportInfo.GetDocumentHeaders(cmd).RemoveAll(h => h.IndexOf("TIME_ZONE", StringComparison.Ordinal) >= 0);
                mb.ExportInfo.GetDocumentFooters().RemoveAll(f => f.IndexOf("TIME_ZONE", StringComparison.Ordinal) >= 0);
                Console.WriteLine($"Exporting {conn.Database} to folder '{folder}'...");
                mb.ExportToFolder(folder);
                Console.WriteLine("Done.");
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
