using Synckit.Db;
using Xunit;

namespace Synckit.Db.Tests;

public class MigratorTests
{
    [Theory]
    [InlineData("1_initial_schema.up.sql", 1)]
    [InlineData("10_add_index.up.sql", 10)]
    [InlineData("2_blobs.up.sql", 2)]
    [InlineData("noprefix.up.sql", 0)]
    public void PrefixNum_ParsesIntegerPrefix(string name, int expected)
    {
        Assert.Equal(expected, Migrator.PrefixNum(name));
    }

    [Fact]
    public void MigrationFiles_OrdersByNumericPrefix_NotLexical()
    {
        var dir = Directory.CreateTempSubdirectory();
        foreach (var n in new[] { "10_x.up.sql", "2_y.up.sql", "1_z.up.sql", "ignore.txt" })
            File.WriteAllText(Path.Combine(dir.FullName, n), "-- sql");

        var files = Migrator.MigrationFiles(dir.FullName);

        Assert.Equal(
            new[] { "1_z.up.sql", "2_y.up.sql", "10_x.up.sql" },
            files.Select(Path.GetFileName).ToArray());
    }
}
