using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Exceptions;
using TestProject.Security;
using TestProject.Tests.Infrastructure;

namespace TestProject.Tests;

public sealed class PathGuardTests : IDisposable
{
    private readonly TempStorageFixture _temp = new();
    private readonly PathGuard _guard;

    public PathGuardTests()
    {
        var env = new FakeEnvironment(Path.GetDirectoryName(_temp.Root)!);
        var storage = new StorageRoot(
            env,
            Options.Create(new FileStorageOptions
            {
                RootPath = _temp.Root,
                SeedDefaultHomeWhenEmpty = false
            }),
            NullLogger<StorageRoot>.Instance);
        storage.EnsureCreated();
        _guard = new PathGuard(storage);
    }

    [Fact]
    public void Resolve_AcceptsValidRelativePath()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Root, "docs"));
        var resolved = _guard.Resolve("docs");
        Assert.Equal("docs", resolved.VirtualPath);
        Assert.True(Directory.Exists(resolved.AbsolutePath));
    }

    [Fact]
    public void Resolve_RejectsParentTraversal()
    {
        var ex = Assert.Throws<FileManagerException>(() => _guard.Resolve("../secret"));
        Assert.Equal("path_traversal", ex.ErrorCode);
    }

    [Fact]
    public void Resolve_RejectsAbsolutePath()
    {
        var ex = Assert.Throws<FileManagerException>(() => _guard.Resolve(@"C:\Windows"));
        Assert.Equal("invalid_path", ex.ErrorCode);
    }

    [Fact]
    public void Resolve_RejectsUncPath()
    {
        var ex = Assert.Throws<FileManagerException>(() => _guard.Resolve(@"\\server\share"));
        Assert.Equal("invalid_path", ex.ErrorCode);
    }

    [Fact]
    public void Resolve_RejectsRootPrefixCollision()
    {
        // Create a sibling directory that shares a string prefix with the root.
        var sibling = _temp.Root + "2";
        Directory.CreateDirectory(sibling);
        try
        {
            var ex = Assert.Throws<FileManagerException>(() =>
            {
                // Force a crafted absolute escape attempt by resolving then checking EnsureInsideRoot via ToVirtualPath.
                var outside = Path.Combine(sibling, "x.txt");
                File.WriteAllText(outside, "x");
                _guard.ToVirtualPath(outside);
            });
            Assert.Equal("path_outside_root", ex.ErrorCode);
        }
        finally
        {
            Directory.Delete(sibling, true);
        }
    }

    [Fact]
    public void ValidateFileName_RejectsInvalidNames()
    {
        Assert.Throws<FileManagerException>(() => _guard.ValidateFileName("a/b"));
        Assert.Throws<FileManagerException>(() => _guard.ValidateFileName("CON"));
        Assert.Throws<FileManagerException>(() => _guard.ValidateFileName("bad."));
        Assert.Throws<FileManagerException>(() => _guard.ValidateFileName("bad "));
    }

    [Fact]
    public void Resolve_RejectsRootDeletionTarget()
    {
        var ex = Assert.Throws<FileManagerException>(() => _guard.Resolve("", mustExist: true, allowRoot: false));
        Assert.Equal("root_protected", ex.ErrorCode);
    }

    [Fact]
    public void Resolve_RejectsStagingAccess()
    {
        var stagingChild = Path.Combine(_temp.Root, StorageRoot.StagingFolderName, "abc");
        Directory.CreateDirectory(stagingChild);
        var ex = Assert.Throws<FileManagerException>(() => _guard.Resolve(".uploads/abc", allowStaging: false));
        Assert.Equal("staging_protected", ex.ErrorCode);
    }

    [Fact]
    public void Resolve_RejectsJunctionEscape()
    {
        var outside = Path.Combine(Path.GetTempPath(), "MapLargeOutside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");

        var junction = Path.Combine(_temp.Root, "escape");
        var created = TryCreateJunction(junction, outside);
        if (!created)
        {
            Directory.Delete(outside, true);
            return; // Environment cannot create junctions; skip without failing the suite.
        }

        try
        {
            var ex = Assert.Throws<FileManagerException>(() => _guard.Resolve("escape"));
            Assert.Equal("reparse_point", ex.ErrorCode);
        }
        finally
        {
            Directory.Delete(junction);
            Directory.Delete(outside, true);
        }
    }

    public void Dispose() => _temp.Dispose();

    private static bool TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process!.WaitForExit(5000);
            return process.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch
        {
            return false;
        }
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public FakeEnvironment(string contentRoot)
        {
            ContentRootPath = contentRoot;
            WebRootPath = Path.Combine(contentRoot, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
        }

        public string ApplicationName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
