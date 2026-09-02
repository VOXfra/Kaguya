using CodeWalker.GameFiles;
using System.Text.Json;

internal static class Program
{
    private const uint Rsc8Magic = 0x38435352;
    private const uint Rsc7Magic = 0x37435352;

    private sealed class BridgeManifest
    {
        public string Source { get; set; } = "";
        public int SourceBytes { get; set; }
        public uint VirtualFlags { get; set; }
        public uint PhysicalFlags { get; set; }
        public int SystemBytes { get; set; }
        public int GraphicsBytes { get; set; }
        public int ClipCount { get; set; }
        public int AnimationCount { get; set; }
        public string[] Clips { get; set; } = Array.Empty<string>();
        public string FirstClip { get; set; } = "";
        public int GtaVYcdBytes { get; set; }
        public string OutputYcd { get; set; } = "";
        public string Strategy { get; set; } = "RSC8 payload -> CodeWalker ClipDictionary -> RSC7/YCD";
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: Rdr2YcdCompat <RDR2 RSC8 .ycd> <output directory>");
            return 2;
        }

        var source = Path.GetFullPath(args[0]);
        var outDir = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outDir);
        var errorPath = Path.Combine(outDir, "bridge-error.txt");

        try
        {
            if (!File.Exists(source)) throw new FileNotFoundException("RDR2 YCD not found", source);
            var bytes = File.ReadAllBytes(source);
            if (bytes.Length < 32) throw new InvalidDataException("RDR2 YCD is too small to be an RSC8 resource.");
            if (BitConverter.ToUInt32(bytes, 0) != Rsc8Magic) throw new InvalidDataException("Expected RSC8 resource header.");

            var virtualFlags = BitConverter.ToUInt32(bytes, 8);
            var physicalFlags = BitConverter.ToUInt32(bytes, 12);
            var systemSize = checked((int)(virtualFlags & 0xFFFFFFF0u));
            var graphicsSize = checked((int)(physicalFlags & 0xFFFFFFF0u));
            var payloadSize = checked(systemSize + graphicsSize);

            if (systemSize <= 0) throw new InvalidDataException($"Invalid RSC8 system size: {systemSize}.");
            if (bytes.Length - 16 < payloadSize)
            {
                throw new InvalidDataException($"RSC8 payload is truncated: header declares {payloadSize} bytes, file only contains {bytes.Length - 16}.");
            }

            var payload = new byte[payloadSize];
            Buffer.BlockCopy(bytes, 16, payload, 0, payload.Length);

            // RDR2 and GTA V both use RAGE system/graphics virtual pointer spaces.
            // This deliberately tries the highest-value compatibility path first:
            // feed the fully decompressed RSC8 payload directly to CodeWalker's
            // mature ClipDictionary reader, then let CodeWalker rebuild a GTA V YCD.
            var reader = new ResourceDataReader(systemSize, graphicsSize, payload);
            var dictionary = reader.ReadBlock<ClipDictionary>();
            if (dictionary == null) throw new InvalidDataException("CodeWalker returned a null ClipDictionary.");

            var ycd = new YcdFile { ClipDictionary = dictionary };
            ycd.InitDictionaries();

            var clipNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ycd.ClipMapEntries != null)
            {
                foreach (var entry in ycd.ClipMapEntries)
                {
                    var name = entry?.Clip?.ShortName;
                    if (!string.IsNullOrWhiteSpace(name)) clipNames.Add(name.Trim());
                }
            }
            if (clipNames.Count == 0) throw new InvalidDataException("ClipDictionary parsed, but no playable clip names were found.");

            var xmlPath = Path.Combine(outDir, "source-rdr2.ycd.xml");
            File.WriteAllText(xmlPath, YcdXml.GetXml(ycd));

            // Keep one human-readable animation dump when CodeWalker can emit it.
            if (ycd.AnimMapEntries != null)
            {
                var firstAnimation = ycd.AnimMapEntries.Select(x => x?.Animation).FirstOrDefault(x => x != null);
                if (firstAnimation != null)
                {
                    try
                    {
                        using var fs = File.Create(Path.Combine(outDir, "source-first-animation.onim"));
                        ycd.SaveOpenFormatsAnimation(firstAnimation, fs);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(outDir, "onim-warning.txt"), ex.ToString());
                    }
                }
            }

            var gtaVBytes = ycd.Save();
            if (gtaVBytes.Length < 16 || BitConverter.ToUInt32(gtaVBytes, 0) != Rsc7Magic)
                throw new InvalidDataException("CodeWalker did not produce a valid RSC7 GTA V resource.");

            var outputYcd = Path.Combine(outDir, "vox_rdr2_bridge.ycd");
            File.WriteAllBytes(outputYcd, gtaVBytes);

            var manifest = new BridgeManifest
            {
                Source = source,
                SourceBytes = bytes.Length,
                VirtualFlags = virtualFlags,
                PhysicalFlags = physicalFlags,
                SystemBytes = systemSize,
                GraphicsBytes = graphicsSize,
                ClipCount = clipNames.Count,
                AnimationCount = ycd.AnimMapEntries?.Count(x => x?.Animation != null) ?? 0,
                Clips = clipNames.ToArray(),
                FirstClip = clipNames.First(),
                GtaVYcdBytes = gtaVBytes.Length,
                OutputYcd = outputYcd,
            };

            File.WriteAllText(Path.Combine(outDir, "bridge-manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            if (File.Exists(errorPath)) File.Delete(errorPath);
            Console.WriteLine($"[BRIDGE-OK] clips={manifest.ClipCount} animations={manifest.AnimationCount}");
            Console.WriteLine($"[BRIDGE-OK] firstClip={manifest.FirstClip}");
            Console.WriteLine($"[BRIDGE-OK] GTA V YCD={outputYcd}");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(errorPath, ex.ToString());
            Console.Error.WriteLine($"[BRIDGE-FAIL] {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
