using System.IO.Compression;
using Pulse.Core.Models;
using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class DropShelfServiceTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string _userDir;
    private readonly string _stagingDir;

    public DropShelfServiceTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "PulseDropShelfTests_" + Guid.NewGuid().ToString("N"));
        _userDir = Path.Combine(_rootDir, "UserFiles");
        _stagingDir = Path.Combine(_rootDir, "Staging");
        Directory.CreateDirectory(_userDir);
        Directory.CreateDirectory(_stagingDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDir))
            {
                Directory.Delete(_rootDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void AddPath_ClassifiesFileImageFolder_AndDeduplicatesCaseInsensitively()
    {
        var docPath = Path.Combine(_userDir, "rapor.pdf");
        var imgPath = Path.Combine(_userDir, "kapak.png");
        var subDir = Path.Combine(_userDir, "ProjeKlasoru");
        File.WriteAllBytes(docPath, new byte[2048]);
        File.WriteAllBytes(imgPath, new byte[512]);
        Directory.CreateDirectory(subDir);

        using var service = new DropShelfService(_stagingDir, maxItems: 6);
        var doc = service.AddPath(docPath);
        var img = service.AddPath(imgPath);
        var dir = service.AddPath(subDir);

        Assert.NotNull(doc);
        Assert.NotNull(img);
        Assert.NotNull(dir);
        Assert.Equal(ShelfItemKind.File, doc!.Kind);
        Assert.Equal("PDF", doc.ExtensionBadge);
        Assert.Equal("2 KB", doc.SizeLabel);

        Assert.Equal(ShelfItemKind.Image, img!.Kind);
        Assert.Equal("PNG", img.ExtensionBadge);

        Assert.Equal(ShelfItemKind.Folder, dir!.Kind);
        Assert.Equal("KLASÖR", dir.ExtensionBadge);
        Assert.Equal("Klasör", dir.SizeLabel);

        // Re-adding existing path moves it to front without duplicating
        _ = service.AddPath(docPath.ToUpperInvariant());
        Assert.Equal(3, service.Items.Count);
        Assert.Equal("rapor.pdf", service.Items[0].DisplayName);
    }

    [Fact]
    public void RemoveAndClear_NeverDeleteUserOriginalFiles()
    {
        var userFile = Path.Combine(_userDir, "onemli.txt");
        File.WriteAllText(userFile, "orijinal veri");

        using var service = new DropShelfService(_stagingDir);
        var item = service.AddPath(userFile, isTemporaryStaging: true); // even if misflagged, outside staging dir!
        Assert.NotNull(item);
        Assert.False(item!.IsTemporaryStaging);

        Assert.True(service.Remove(item.Id));
        Assert.Empty(service.Items);
        Assert.True(File.Exists(userFile));
    }

    [Fact]
    public async Task StageTextAsync_CreatesTempFile_AndDeletesOnRemoveOrClear()
    {
        using var service = new DropShelfService(_stagingDir);
        var item = await service.StageTextAsync("https://github.com/sandrotonal/pulse\nikinci satir");

        Assert.NotNull(item);
        Assert.Equal(ShelfItemKind.Text, item!.Kind);
        Assert.True(item.IsTemporaryStaging);
        Assert.True(File.Exists(item.FilePath));

        var path = item.FilePath;
        Assert.True(service.Remove(item.Id));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task MaxItemsCap_EvictsOldestAndDeletesEvictedStagingTempOnly()
    {
        var userFile = Path.Combine(_userDir, "a.txt");
        File.WriteAllText(userFile, "hello");

        using var service = new DropShelfService(_stagingDir, maxItems: 2);
        var staged1 = await service.StageTextAsync("first note");
        Assert.NotNull(staged1);
        var staged1Path = staged1!.FilePath;

        _ = service.AddPath(userFile);
        var staged2 = await service.StageTextAsync("third note");
        Assert.NotNull(staged2);

        Assert.Equal(2, service.Items.Count);
        Assert.False(File.Exists(staged1Path));
        Assert.True(File.Exists(userFile));
    }

    [Fact]
    public async Task CreateZipArchiveAsync_PackagesFilesAndFoldersIntoValidZip()
    {
        var fileA = Path.Combine(_userDir, "not.txt");
        File.WriteAllText(fileA, "merhaba pulse");
        var folder = Path.Combine(_userDir, "Varliklar");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "veri.json"), "{\"ok\":true}");

        using var service = new DropShelfService(_stagingDir);
        service.AddPath(fileA);
        service.AddPath(folder);

        var zipItem = await service.CreateZipArchiveAsync();
        Assert.NotNull(zipItem);
        Assert.True(zipItem!.IsTemporaryStaging);
        Assert.Equal("ZIP", zipItem.ExtensionBadge);
        Assert.True(File.Exists(zipItem.FilePath));

        using (var archive = ZipFile.OpenRead(zipItem.FilePath))
        {
            var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("not.txt", names);
            Assert.Contains("Varliklar/veri.json", names);
        }

        // Clearing removes the temporary zip while keeping original user files safe
        service.Clear();
        Assert.False(File.Exists(zipItem.FilePath));
        Assert.True(File.Exists(fileA));
    }
}
