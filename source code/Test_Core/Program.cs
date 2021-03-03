using MySql.Data.MySqlClient;
using System;

namespace Test_Core
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Please pass connection string as first parameter and destination folder as second parameter.");
                return;
            }

            Console.Write("Writing to folder " + args[1] + "... ");
            using (MySqlConnection conn = new MySqlConnection(args[0]))
            {
                MySqlCommand cmd = new MySqlCommand();
                MySqlBackup mb = new MySqlBackup(cmd);
                cmd.Connection = conn;
                conn.Open();

                mb.ExportToFolder(args[1]);
            }

            Console.WriteLine("done.");
            Console.WriteLine();
            Console.Write("Reading from folder " + args[1] + "... ");

            using (MySqlConnection conn = new MySqlConnection(args[0]))
            {
                MySqlCommand cmd = new MySqlCommand();
                MySqlBackup mb = new MySqlBackup(cmd);
                cmd.Connection = conn;
                conn.Open();
                mb.ImportFromDirectory(args[1]);
            }

            Console.WriteLine("done.");
        }
    }
}
