using MySql.Data.MySqlClient;
using System;

namespace MySqlRestoreFolder
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: MySqlRestoreFolder <connectionString> <sourceFolder>");
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
                Console.WriteLine($"Restoring from folder '{folder}' into {conn.Database}...");
                mb.ImportFromDirectory(folder);
                Console.WriteLine("Done.");
            }

            return 0;
        }
    }
}
