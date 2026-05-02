using MySql.Data.MySqlClient;
using System;

namespace MySqlBackupFolder
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: MySqlBackupFolder <connectionString> <destinationFolder>");
                return 1;
            }

            string connectionString = args[0];
            string folder = args[1];

            using (var conn = new MySqlConnection(connectionString))
            using (var cmd = new MySqlCommand())
            {
                conn.Open();
                cmd.Connection = conn;

                var mb = new MySqlBackup(cmd);
                Console.WriteLine($"Exporting {conn.Database} to folder '{folder}'...");
                mb.ExportToFolder(folder);
                Console.WriteLine("Done.");
            }

            return 0;
        }
    }
}
