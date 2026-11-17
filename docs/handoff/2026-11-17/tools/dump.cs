#:package Microsoft.Data.Sqlite@10.0.14
// Dumps what a crawl file holds for one URL, decompressed, for the check audit.
// dotnet run dump.cs -- <crawl.sicrawl> html|details <url-id or full URL>
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: dump.cs <crawl.sicrawl> html|details <url-id or URL>");
    return 2;
}

var (path, what, key) = (args[0], args[1], args[2]);
using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
connection.Open();

long id;
if (long.TryParse(key, out var parsed))
{
    id = parsed;
}
else
{
    using var find = connection.CreateCommand();
    find.CommandText = "SELECT id FROM urls WHERE url = $url";
    find.Parameters.AddWithValue("$url", key);
    if (find.ExecuteScalar() is not long found)
    {
        Console.Error.WriteLine($"No URL {key}");
        return 1;
    }
    id = found;
}

using var read = connection.CreateCommand();
read.CommandText = what == "html"
    ? "SELECT html FROM response_html WHERE url_id = $id"
    : "SELECT data FROM response_details WHERE url_id = $id";
read.Parameters.AddWithValue("$id", id);
if (read.ExecuteScalar() is not byte[] packed)
{
    Console.Error.WriteLine($"Nothing stored for URL {id}");
    return 1;
}

using var brotli = new BrotliStream(new MemoryStream(packed), CompressionMode.Decompress);
using var reader = new StreamReader(brotli, Encoding.UTF8);
Console.OutputEncoding = Encoding.UTF8;
Console.Out.Write(reader.ReadToEnd());
return 0;
