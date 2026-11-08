using System.Diagnostics;
using System.Text;
using TestProject.Models;
using TestProject.Security;

namespace TestProject.Services;

/// <summary>
/// Creates the Star Wars demonstration tree under the storage root.
/// Existing directories and files are left unchanged. Staging (.uploads) is never touched.
/// </summary>
public sealed class DemoDataService : IDisposable
{
    /// <summary>
    /// Folders under 00-Corporate through 07-Media-Assets, including the eight field-report planets.
    /// </summary>
    public const int NarrativeDirectoryCount = 72;

    /// <summary>Hand-authored files, including the storage-root samples.</summary>
    public const int NarrativeFileCount = 146;

    /// <summary>Region folders inside 08-Scale-100x.</summary>
    public const int RegionCount = 100;

    /// <summary>Sector folders inside each region.</summary>
    public const int SectorsPerRegion = 74;

    /// <summary>Flat files in each of the two large paging directories.</summary>
    public const int LargeDirectoryFileCount = 3500;

    private const string ScaleRootName = "08-Scale-100x";

    private static readonly string[] ScaleExtensions =
    [
        ".md", ".pdf", ".docx", ".xlsx", ".png", ".svg", ".jpg", ".txt"
    ];

    private static readonly string[] FieldPlanets =
    [
        "Alderaan", "Bespin", "Coruscant", "Geonosis", "Jakku", "Kamino", "Kashyyyk", "Naboo"
    ];

    private static readonly string[] InsigniaNames =
    [
        "deathstar-schematic", "empire-crest", "endor-map", "hoth-map",
        "jedi-crest", "rebel-crest", "senate-crest", "tatooine-map"
    ];

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly StorageRoot _storage;
    private readonly ILogger<DemoDataService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DemoDataService(StorageRoot storage, ILogger<DemoDataService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Directories a full seed creates: narrative folders, the scale root, two large folders, and every region/sector.
    /// </summary>
    public static int ExpectedDirectoryCount =>
        NarrativeDirectoryCount + 1 + 2 + RegionCount + (RegionCount * SectorsPerRegion);

    /// <summary>
    /// Files a full seed creates: narrative files, both large directories, and one file per sector.
    /// </summary>
    public static int ExpectedFileCount =>
        NarrativeFileCount + (LargeDirectoryFileCount * 2) + (RegionCount * SectorsPerRegion);

    /// <summary>
    /// Creates any missing demo folders and files. Safe to call again.
    /// </summary>
    public async Task<DemoSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var started = Stopwatch.StartNew();
            var stats = new SeedStats();
            var root = _storage.RootPath;
            Directory.CreateDirectory(root);

            SeedNarrative(root, stats, cancellationToken);
            SeedScale(root, stats, cancellationToken);

            started.Stop();
            _logger.LogInformation(
                "Demo seed created {Directories} directories and {Files} files; skipped {Skipped} existing entries in {Elapsed} ms.",
                stats.CreatedDirectories,
                stats.CreatedFiles,
                stats.SkippedExisting,
                started.ElapsedMilliseconds);

            return new DemoSeedResult
            {
                CreatedDirectories = stats.CreatedDirectories,
                CreatedFiles = stats.CreatedFiles,
                SkippedExisting = stats.SkippedExisting,
                ElapsedMilliseconds = started.ElapsedMilliseconds
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Themed folders several levels deep, plus the mixed-type files at the storage root.
    /// </summary>
    private void SeedNarrative(string root, SeedStats stats, CancellationToken cancellationToken)
    {
        foreach (var relative in NarrativeDirectories)
        {
            EnsureDirectory(root, relative, stats, cancellationToken);
        }

        foreach (var planet in FieldPlanets)
        {
            EnsureDirectory(root, $"05-Planetary-Ops/_Field-Reports/{planet}", stats, cancellationToken);
            EnsureFile(root, $"05-Planetary-Ops/_Field-Reports/{planet}/notes.md", $"Field notes for {planet}.\n", stats, cancellationToken);
            EnsureFile(root, $"05-Planetary-Ops/_Field-Reports/{planet}/report.docx", $"Field report for {planet}.\n", stats, cancellationToken);
            EnsureFile(root, $"05-Planetary-Ops/_Field-Reports/{planet}/sitrep.txt", $"Sitrep: {planet} stable.\n", stats, cancellationToken);
            EnsureFile(root, $"05-Planetary-Ops/_Field-Reports/{planet}/summary.pdf", $"Summary for {planet}.\n", stats, cancellationToken);
        }

        for (var user = 1; user <= 12; user++)
        {
            EnsureFile(
                root,
                $"06-Shared-Services/IT/Identity/user-{user:00}.txt",
                $"Service account user-{user:00} for shared identity.\n",
                stats,
                cancellationToken);
        }

        for (var incident = 1; incident <= 8; incident++)
        {
            EnsureFile(
                root,
                $"06-Shared-Services/IT/Security/incident-{incident:00}.md",
                $"Incident {incident:00} closed. No data loss.\n",
                stats,
                cancellationToken);
        }

        for (var slide = 1; slide <= 10; slide++)
        {
            EnsureFile(
                root,
                $"07-Media-Assets/Briefing-Slides/briefing-{slide:00}.pdf",
                $"Briefing slide deck {slide:00}.\n",
                stats,
                cancellationToken);
            EnsureFile(
                root,
                $"07-Media-Assets/Briefing-Slides/speaker-notes-{slide:00}.docx",
                $"Speaker notes for briefing {slide:00}.\n",
                stats,
                cancellationToken);
        }

        foreach (var name in InsigniaNames)
        {
            EnsureFile(root, $"07-Media-Assets/Insignia/{name}.png", $"Insignia raster {name}.\n", stats, cancellationToken);
            EnsureFile(root, $"07-Media-Assets/Insignia/{name}.svg", BannerSvg(name, "#1b2838"), stats, cancellationToken);
            EnsureFile(root, $"07-Media-Assets/Maps/{name}.jpg", $"Map image {name}.\n", stats, cancellationToken);
        }

        foreach (var (relative, content) in NarrativeFiles)
        {
            EnsureFile(root, relative, content, stats, cancellationToken);
        }
    }

    /// <summary>
    /// Wide region/sector tree plus two flat directories large enough to exercise paging.
    /// </summary>
    private void SeedScale(string root, SeedStats stats, CancellationToken cancellationToken)
    {
        EnsureDirectory(root, ScaleRootName, stats, cancellationToken);
        SeedLargeDirectory(root, "Large-Directory-Alpha", "alpha", stats, cancellationToken);
        SeedLargeDirectory(root, "Large-Directory-Beta", "beta", stats, cancellationToken);

        for (var region = 1; region <= RegionCount; region++)
        {
            var regionRelative = $"{ScaleRootName}/Region-{region:000}";
            EnsureDirectory(root, regionRelative, stats, cancellationToken);

            for (var sector = 1; sector <= SectorsPerRegion; sector++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = ScaleExtensions[(sector - 1) % ScaleExtensions.Length];
                var sectorRelative = $"{regionRelative}/Sector-{sector:000}";
                EnsureDirectory(root, sectorRelative, stats, cancellationToken);
                EnsureFile(
                    root,
                    $"{sectorRelative}/asset-{region:000}-{sector:000}{extension}",
                    $"Star Wars scale demo Region {region:000} Sector {sector:000}\n",
                    stats,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Writes a flat folder of numbered files whose extensions cycle through the demo types.
    /// </summary>
    private void SeedLargeDirectory(string root, string folderName, string prefix, SeedStats stats, CancellationToken cancellationToken)
    {
        var relativeFolder = $"{ScaleRootName}/{folderName}";
        EnsureDirectory(root, relativeFolder, stats, cancellationToken);

        for (var index = 1; index <= LargeDirectoryFileCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = ScaleExtensions[(index - 1) % ScaleExtensions.Length];
            EnsureFile(
                root,
                $"{relativeFolder}/{prefix}-{index:0000}{extension}",
                $"Scale demo file {prefix} {index}\n",
                stats,
                cancellationToken);
        }
    }

    private void EnsureDirectory(string root, string relative, SeedStats stats, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var absolute = ToAbsolute(root, relative);
        if (Directory.Exists(absolute))
        {
            stats.SkippedExisting++;
            return;
        }

        Directory.CreateDirectory(absolute);
        stats.CreatedDirectories++;
    }

    private void EnsureFile(string root, string relative, string content, SeedStats stats, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var absolute = ToAbsolute(root, relative);
        if (File.Exists(absolute))
        {
            stats.SkippedExisting++;
            return;
        }

        var parent = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(absolute, content, Utf8);
        stats.CreatedFiles++;
    }

    /// <summary>
    /// Resolves a slash-separated relative path and rejects anything that would leave the storage root.
    /// </summary>
    private static string ToAbsolute(string root, string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidOperationException("Demo data path was rejected.");
        }

        var combined = root;
        foreach (var part in parts)
        {
            combined = Path.Combine(combined, part);
        }

        var full = Path.GetFullPath(combined);
        var rootFull = Path.GetFullPath(root);
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Demo data path was rejected.");
        }

        return full;
    }

    private static string BannerSvg(string label, string color) =>
        $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
          <rect width="640" height="360" fill="{color}"/>
          <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">{label}</text>
        </svg>
        """;

    private static readonly string[] NarrativeDirectories =
    [
        "00-Corporate",
        "00-Corporate/Communications",
        "00-Corporate/Executive-Office",
        "00-Corporate/Legal-Counsel",
        "01-Galactic-Empire",
        "01-Galactic-Empire/Communications",
        "01-Galactic-Empire/Finance",
        "01-Galactic-Empire/Finance/Budget-FY25",
        "01-Galactic-Empire/Fleet-Command",
        "01-Galactic-Empire/Fleet-Command/Star-Destroyers",
        "01-Galactic-Empire/Fleet-Command/TIE-Operations",
        "01-Galactic-Empire/HR",
        "01-Galactic-Empire/HR/Stormtrooper-Recruiting",
        "01-Galactic-Empire/Intelligence",
        "01-Galactic-Empire/Intelligence/Death-Star-Archives",
        "01-Galactic-Empire/Intelligence/ISB",
        "02-Rebel-Alliance",
        "02-Rebel-Alliance/Fleet",
        "02-Rebel-Alliance/Fleet/Mon-Calamari-Cruisers",
        "02-Rebel-Alliance/Fleet/X-Wing-Squadrons",
        "02-Rebel-Alliance/High-Command",
        "02-Rebel-Alliance/Intelligence",
        "02-Rebel-Alliance/Intelligence/Bothan-Network",
        "02-Rebel-Alliance/Logistics",
        "02-Rebel-Alliance/Logistics/Supply-Hoth",
        "02-Rebel-Alliance/Logistics/Supply-Yavin",
        "02-Rebel-Alliance/Logistics/Supply-Yavin/Test Folder",
        "03-Jedi-Order",
        "03-Jedi-Order/Archives",
        "03-Jedi-Order/Archives/Holocrons",
        "03-Jedi-Order/Archives/Lost-Texts",
        "03-Jedi-Order/Temple-Operations",
        "03-Jedi-Order/Temple-Operations/Coruscant",
        "03-Jedi-Order/Training",
        "03-Jedi-Order/Training/Padawans",
        "03-Jedi-Order/Training/Younglings",
        "04-Senate",
        "04-Senate/Committees",
        "04-Senate/Committees/Appropriations",
        "04-Senate/Committees/Defense",
        "04-Senate/Delegations",
        "04-Senate/Delegations/Alderaan",
        "04-Senate/Delegations/Naboo",
        "05-Planetary-Ops",
        "05-Planetary-Ops/Endor",
        "05-Planetary-Ops/Endor/Shield-Generator",
        "05-Planetary-Ops/Hoth",
        "05-Planetary-Ops/Hoth/Echo-Base",
        "05-Planetary-Ops/Mustafar",
        "05-Planetary-Ops/Mustafar/Mining",
        "05-Planetary-Ops/Tatooine",
        "05-Planetary-Ops/Tatooine/Jundland-Wastes",
        "05-Planetary-Ops/Tatooine/Mos-Eisley",
        "05-Planetary-Ops/_Field-Reports",
        "06-Shared-Services",
        "06-Shared-Services/Facilities",
        "06-Shared-Services/IT",
        "06-Shared-Services/IT/Identity",
        "06-Shared-Services/IT/Security",
        "06-Shared-Services/Vendor-Management",
        "07-Media-Assets",
        "07-Media-Assets/Briefing-Slides",
        "07-Media-Assets/Insignia",
        "07-Media-Assets/Maps"
    ];

    private static readonly (string Relative, string Content)[] NarrativeFiles =
    [
        ("README-DEMO.txt", "Star Wars Enterprise Demo Dataset\nGenerated for MapLarge File Manager demonstration.\nExisting files are left in place when this set is generated again.\n"),
        ("CHANGELOG.md", "# Demo changelog\n\n- Initial Star Wars file tree for paging and search demos.\n"),
        ("Welcome-Briefing.docx", "Welcome briefing for the galaxy file demonstration.\n"),
        ("Org-Chart-Index.xlsx", "Organization index: Corporate, Empire, Alliance, Jedi, Senate, Planetary Ops.\n"),
        ("Security-Policy.pdf", "Demo security policy. Do not store credentials in this tree.\n"),
        ("galaxy-banner.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#0b1c3a"/>
              <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">Galaxy Far Far Away</text>
            </svg>
            """),
        ("status-green.png", "Status swatch: green.\n"),
        ("hero-banner.jpg", "Hero banner placeholder for the demo root.\n"),
        ("01-Galactic-Empire/Communications/imperial-banner.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#2a0000"/>
              <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">Imperial Banner</text>
            </svg>
            """),
        ("01-Galactic-Empire/Communications/imperial-crest.png", "Imperial crest placeholder.\n"),
        ("01-Galactic-Empire/Finance/Budget-FY25/imperial-budget.xlsx", "FY25 imperial budget worksheet.\n"),
        ("01-Galactic-Empire/Fleet-Command/Star-Destroyers/fleet-roster.txt", "Devastator, Executor, Avenger.\n"),
        ("01-Galactic-Empire/Fleet-Command/Star-Destroyers/maintenance.md", "# Maintenance\n\nHyperdrive inspection due.\n"),
        ("01-Galactic-Empire/Fleet-Command/TIE-Operations/galaxy-banner.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#111111"/>
              <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">TIE Operations</text>
            </svg>
            """),
        ("01-Galactic-Empire/Fleet-Command/TIE-Operations/sortie-log.xlsx", "Sortie log for TIE wings.\n"),
        ("01-Galactic-Empire/Fleet-Command/TIE-Operations/TIE-Pilot-Handbook.docx", "TIE pilot handbook excerpt.\n"),
        ("01-Galactic-Empire/HR/Stormtrooper-Recruiting/onboarding.docx", "Stormtrooper onboarding checklist.\n"),
        ("01-Galactic-Empire/Intelligence/Death-Star-Archives/construction-notes.md", "# Construction\n\nThermal exhaust reviewed.\n"),
        ("01-Galactic-Empire/Intelligence/ISB/surveillance-summary.pdf", "ISB surveillance summary.\n"),
        ("02-Rebel-Alliance/galaxy-banner.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#0e2a1a"/>
              <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">Rebel Alliance</text>
            </svg>
            """),
        ("02-Rebel-Alliance/Fleet/Mon-Calamari-Cruisers/home-one-specs.pdf", "Home One cruiser specifications.\n"),
        ("02-Rebel-Alliance/Fleet/X-Wing-Squadrons/kill-board.xlsx", "X-wing squadron kill board.\n"),
        ("02-Rebel-Alliance/Fleet/X-Wing-Squadrons/Red-Squadron-Roster.docx", "Red Squadron roster.\n"),
        ("02-Rebel-Alliance/High-Command/orders.txt", "Hold at Yavin. Await Bothan confirmation.\n"),
        ("02-Rebel-Alliance/Intelligence/Bothan-Network/intel-brief.md", "# Intel brief\n\nShield status confirmed.\n"),
        ("02-Rebel-Alliance/Logistics/Supply-Hoth/hoth-route.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#d7e6f2"/>
              <text x="320" y="190" text-anchor="middle" fill="#123" font-size="28">Hoth Route</text>
            </svg>
            """),
        ("02-Rebel-Alliance/Logistics/Supply-Hoth/manifest.txt", "Echo Base supply manifest.\n"),
        ("02-Rebel-Alliance/Logistics/Supply-Yavin/galaxy-banner.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#1d3b16"/>
              <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">Yavin Depot</text>
            </svg>
            """),
        ("02-Rebel-Alliance/Logistics/Supply-Yavin/yavin-depot.jpg", "Yavin depot photo placeholder.\n"),
        ("03-Jedi-Order/Archives/Holocrons/index.md", "# Holocrons\n\nCatalog of recovered archives.\n"),
        ("03-Jedi-Order/Archives/Holocrons/jedi-glyph.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#10243f"/>
              <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">Jedi Glyph</text>
            </svg>
            """),
        ("03-Jedi-Order/Archives/Lost-Texts/kyber-research.docx", "Kyber crystal research notes.\n"),
        ("03-Jedi-Order/Temple-Operations/Coruscant/temple-map.png", "Coruscant temple map placeholder.\n"),
        ("03-Jedi-Order/Temple-Operations/Coruscant/temple-security.pdf", "Temple security rotation.\n"),
        ("03-Jedi-Order/Training/Padawans/lesson-plan.txt", "Lesson: balance and patience.\n"),
        ("03-Jedi-Order/Training/Younglings/attendance.xlsx", "Youngling attendance sheet.\n"),
        ("04-Senate/Committees/Appropriations/budget-hearing.docx", "Appropriations hearing minutes.\n"),
        ("04-Senate/Committees/Defense/defense-votes.xlsx", "Defense committee vote tally.\n"),
        ("04-Senate/Delegations/Alderaan/position-paper.md", "# Alderaan\n\nOpposition to military expansion.\n"),
        ("04-Senate/Delegations/Naboo/naboo-seal.jpg", "Naboo seal placeholder.\n"),
        ("04-Senate/Delegations/Naboo/trade-agreement.pdf", "Naboo trade agreement summary.\n"),
        ("05-Planetary-Ops/Endor/Shield-Generator/forest-cam.png", "Endor forest camera still.\n"),
        ("05-Planetary-Ops/Endor/Shield-Generator/inventory.xlsx", "Shield generator spare inventory.\n"),
        ("05-Planetary-Ops/Hoth/Echo-Base/evacuation-plan.pdf", "Echo Base evacuation plan.\n"),
        ("05-Planetary-Ops/Hoth/Echo-Base/ice-field.jpg", "Hoth ice field placeholder.\n"),
        ("05-Planetary-Ops/Mustafar/Mining/lava-hazard.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="640" height="360" viewBox="0 0 640 360">
              <rect width="640" height="360" fill="#4a1208"/>
              <text x="320" y="190" text-anchor="middle" fill="#ffe81f" font-size="28">Lava Hazard</text>
            </svg>
            """),
        ("05-Planetary-Ops/Tatooine/Jundland-Wastes/survey.docx", "Jundland moisture survey.\n"),
        ("05-Planetary-Ops/Tatooine/Mos-Eisley/cantina-notes.txt", "Mos Eisley docking notes.\n"),
        ("06-Shared-Services/Facilities/lease-coruscant.docx", "Coruscant facilities lease abstract.\n"),
        ("06-Shared-Services/Vendor-Management/vendors.xlsx", "Approved vendor list.\n")
    ];

    private sealed class SeedStats
    {
        public int CreatedDirectories { get; set; }

        public int CreatedFiles { get; set; }

        public int SkippedExisting { get; set; }
    }
}
