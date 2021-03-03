using MySql.Data.MySqlClient;
using System.Xml.Linq;

namespace MySqlBackupFolder
{
    class Program
    {
        static void Main(string[] args)
        {
            XDocument xdoc = XDocument.Load("talete.net/Web.config");
            var path = xdoc.Element("configuration").Element("connectionStrings").Element("add").Attribute("connectionString").Value;

            using (MySqlConnection conn = new MySqlConnection(path))
            {
                MySqlCommand cmd = new MySqlCommand();
                MySqlBackup mb = new MySqlBackup(cmd);
                cmd.Connection = conn;
                conn.Open();

                mb.ExportToFolder("talete_development");
            }
        }
    }
}
