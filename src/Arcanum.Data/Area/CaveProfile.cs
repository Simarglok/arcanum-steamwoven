namespace Arcanum.Data.Area;

// ARC-WORLD-009: pinned original profile of the Crash Site cave, catalogue map 5078
// (runtime 79), from docs/research/cave-transition-evidence.md. Safe numeric
// identities only; no original payload, logical names or pixel corpus is embedded.
internal static class CaveProfile
{
    internal const int CatalogueMap = 5078;
    internal const int RuntimeMap = 79;
    internal const long Sector = 2080374816;
    internal const long NormalGameMobileBytes = 6698;
    internal const string NormalGameMobileSha256 = "aa29827de32c5cb6bf7aa03ea049f9f01d7698b003097172cd26ab80c9658318";

    internal static readonly IReadOnlyDictionary<int, (long Length, string Sha256)> Prototypes = new Dictionary<int, (long, string)>
    {
        [1020] = new(377, "7e19f40b2b4db369cd7dbca77c1e2cc360dfc821196dc0057e7ec0968cc5fcd8"),
        [3043] = new(398, "fa611c4de0ec286c54d8c4951f5b41236001ec47f62912617d58761c55752941"),
        [3045] = new(398, "14b1870595eb3a0310ced051bd9768aadb23bfdad8df430b546675ce77b2a11f"),
        [4037] = new(414, "70d70e48d45bf1d9f3678dc6b0315f49fd02200fc3cb4c8e1086702759c891ff"),
        [4038] = new(414, "2531e1a1c6ddca2f483a2d557bebcfd6c1b0b89bd2d996a3f559adc28924214b"),
        [4040] = new(378, "6e08c16d7d338e3681c610493d9f8e7514b40819e526e470341fda002599b9f4"),
        [4045] = new(414, "788c41d7b6d18f3bb4b07eaa5c6445b5cac18e8d4e526aef7b929c215aa42259"),
        [4047] = new(422, "92d36db76ba575fd8377a2dc716f2af1b8e00ce343a3bf7dc866cfdc86bffd3d"),
        [4048] = new(378, "ec807b77ea283d8d84387993a439953b7acf1a1ef7bdfb30b6a86a334dcfe8d1"),
        [6067] = new(599, "b2c4f025bd3073d137926af892374ba7ca4c5949dd01b3ccee4b325c586ef10b"),
        [6102] = new(599, "92bd0dfba9d7366abaa94a2ebf5c914f62f85c493d61787dd2ab36a053370505"),
        [7058] = new(472, "61ea637e9bcc62dff206871ef98750ef056976d3f36328a128c86487ba762066"),
        [7059] = new(472, "3477b86b90264fabdaaa0791282f194a5896a6a82cd704dfc62be6de96f2b1f0"),
        [9076] = new(468, "fb319ab91ae047c255fce732342b180112b7bff55d364d8ff71713b844996393"),
        [10080] = new(464, "b6ec029d0b29b1e463d56c341fbb3a5be0bd8507219e676e33547644c3eb3a7d"),
        [10094] = new(500, "65ebfda723e3e536ed3962d3c56b789a642c3e1a0077b7367593c4a2f8031290"),
        [10128] = new(464, "e0e89e51f4547bc8240023f656a19370b324981014e080afe29e2d8072076455"),
        [15091] = new(464, "0159bcece3d4fc20e1e772a16b9a87315f5d21808246de9da342b2be6eeefe5e"),
        [15095] = new(464, "dbb6978591984e18bb0aad8af33679deef8e0ff784ae4733b290759477c8d0fe"),
        [15099] = new(464, "41ecae3faf43a7a8e2ffb372af726f50e197a1c0a1974ac983af95653bfbac8b"),
        [15106] = new(464, "bf31d63b3af0376dfc4eb0fad07333a31e62827c02974aa4c143b59e9343ed42"),
        [15138] = new(464, "305cd3c9cdd60a0c66f5bf8de0950037fc151c552a5e52f0832a30185d74dfc3"),
        [15152] = new(464, "9570125d12e79162a7dc5a43b3ea20134d909ab270a0e2bf8de3a50da02bf853"),
        [15154] = new(464, "bcfbbaded3471953a788ac7ba0423a70b25a5d088fb2f2dae4203490dca3f266"),
        [15158] = new(464, "11e7550a5a0ffafbc4200697c7cce34f38a0cbf422777a217d0824f78188cebe"),
        [15164] = new(464, "eade84e7864500797f18ffc566f63937273011c88369983751129f8aa47cf5e7"),
        [15168] = new(464, "89067bc6f55a419759e31cf7e00cb63aad41f4385754e58f485ea3bd06d802e8"),
        [15172] = new(464, "b3b0517e807726f8f822daf07ce3febbf891121a4d0bcbdb97650405c92ce3d5"),
        [17108] = new(952, "a5b627e21d2aaf0db8d917c4d8bc85456d7c40565468325d88952eaecac7eaa4"),
        [28373] = new(920, "470d433a5c0bfbaace8d857bc7298029ea5c771539b380d564ecd034b16c9b86"),
    };

    internal static AreaSourcePlan Plan()
    {
        var identities = new List<AreaInputIdentity>
        {
            new(new(AreaSourceRole.Module), 355938305, "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f"),
            new(new(AreaSourceRole.Resource1), 447470816, "6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576"),
            new(new(AreaSourceRole.Resource2), 147991122, "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d"),
            new(new(AreaSourceRole.NormalGameMobile), NormalGameMobileBytes, NormalGameMobileSha256),
        };
        identities.AddRange(Prototypes.OrderBy(p => p.Key).Select(p => new AreaInputIdentity(new(AreaSourceRole.Prototype, p.Key), p.Value.Length, p.Value.Sha256)));
        AreaMappingBinding Map(AreaMappingRole role, AreaSourceRole source, int ordinal, uint stored, uint decoded, uint offset, string name, string payload) => new(role, new(new(source), ordinal, 2, stored, decoded, offset, name), payload);
        var mappings = new[]
        {
            Map(AreaMappingRole.Tile, AreaSourceRole.Resource2, 5353, 1375, 2999, 71212397, "685c98e637234501e2c610382fc987f89ea3c29319b0b4ea8bbd5c349d698d75", "1edd402a3d7b786c01e0500f1534c76525cae1cd83fc67df274afd036d6a9627"),
            Map(AreaMappingRole.Facade, AreaSourceRole.Resource2, 231, 2359, 7612, 17074700, "cc05e6ba27e6d5d47bffc5dc13ae9f326e588ddd928c075e3e6dcf0b37206a8d", "af539bf3d091f15062d5f45582265e2d34f1ff06448744dea12b3363870c45a5"),
            Map(AreaMappingRole.Scenery, AreaSourceRole.Resource2, 3691, 10388, 40369, 63462086, "83ad58145c2fd0b84385c7b092b59568bd1f7cc97b9b215c9398760f6f2484b4", "80b0d016de1ef46aa7a3f9dfc74aea97f33fb70fa286ac72d03cbd1b081c1781"),
            Map(AreaMappingRole.Container, AreaSourceRole.Resource2, 34, 774, 2304, 163920, "e560665a361f3dfe166828df579b3526e2ef35a071e39d39f882925dfc2af353", "eb69e59f9c53f894e1b8b4811a8d31145324c21dd31313b022493e2674679321"),
            Map(AreaMappingRole.ItemGround, AreaSourceRole.Resource2, 1705, 5383, 23290, 46000847, "259acd9431f31a5e550d3d3145b9fe48f266755c5b7c0795a96280e34108384b", "f30fc1f5fb4ad2963986950f08e04efd043ad896f0f5471c2945a14d6728bb1c"),
            Map(AreaMappingRole.MonsterNames, AreaSourceRole.Resource1, 4915, 408, 750, 319999124, "41f64fcdf9a0137973d29a6edb282af0a3a98431bdb87246f40d1c943e86f9c7", "db3ffecf9845e7d9906c19d0ea91289d90147ba2496aac6d275a9cc017e5a18a"),
            Map(AreaMappingRole.UniqueNpcNames, AreaSourceRole.Resource1, 5847, 237, 552, 443052894, "224e1197160e88c683dc7ce54cf27e92d5f6d18ae72cf01c421cbd3955b6d20b", "611e82ba4e99498aaacb3e9527ef610b4939e704452182523e9c72a5a1e3c541"),
            Map(AreaMappingRole.MissingPolicy, AreaSourceRole.Resource1, 4695, 108, 332, 278084522, "90de5bb07348cecfc2bc60cfb68d8b56493a8f36ae906da63d1ec6499f84b3a6", "49502ba4e4236f7c7835e697c3ff569f894f2814f82cd3543ce861c28526b7e0"),
            Map(AreaMappingRole.WallStructure, AreaSourceRole.Resource2, 7420, 1300, 3402, 81663721, "aef55e332c0b73e1844f6667aa5c0a23610efd223ec95c8e36e58d32e69697d6", "5128adf3e7acbb8d5f1a44d811acdd47a0cc400c003e3cb2409da822310cf8e1"),
            Map(AreaMappingRole.WallName, AreaSourceRole.Resource2, 8354, 466, 910, 87012024, "cd4bae5237d96bba39d8aae531959f39e946d30653169e6b4f2c943e2f6d45bd", "79f2b405885e431ccffedcba7fe36ffaffbf860a0302b7f1495ae402e3621e8b"),
        };
        return new(CatalogueMap, RuntimeMap, new(32, 31), 141, "maps/Crash Site Cave", identities, mappings) { OriginalMap = OriginalAreaMap.CrashSiteCave };
    }

    internal static readonly AreaReconciliation ExpectedCounts = new(1, 1, 0, 8, 4096, 0, 138, 39, 39, 0, 0, 0, 39, 30, 194);

    internal static void Check(AreaReconciliation counts, IEnumerable<AreaSectorRecord> sectors, IEnumerable<AreaObjectRecord> objects, IEnumerable<AreaVisualRecord> visuals)
    {
        if (counts != ExpectedCounts)
            throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Publication));
        var core = sectors.Where(s => s.Scope == AreaScope.Core).ToArray();
        if (core.Length != 1 || core[0].Key.Packed != Sector || core[0].LightCount != 4)
            throw new AreaException(new(AreaErrorCode.InvalidSelection, AreaStage.Selection));
        if (objects.Any(o => !o.SchemaReady || !o.PlacementReady || !o.RequiredLayersReady) || objects.Count(o => o.Type == 0) != 125 ||
            visuals.Count(v => v.Key.Kind == AreaSourceKind.Static) != 138 || visuals.Count(v => v.Key.Kind == AreaSourceKind.Mobile) != 39)
            throw new AreaException(new(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Publication));
    }

    // Exact exceptions, each pinned to the object, record, raw selector and full ART entry:
    // four contained items whose ground ART carries trailing bytes after its last frame
    // (one NUL for entry 919, the rule the exterior profile pins for it; the pair 02 00 for
    // entries 955 and 956), viewed without them; and one container whose single-rotation
    // ART is selected with a nonzero rotation (read as its only group).
    private static readonly IReadOnlyDictionary<int, (uint Raw, string Object, string Record, int Ordinal, uint Stored, uint Decoded, uint Offset, string Name, string Sha, string Tail, string View)> TrailingBytes =
        new Dictionary<int, (uint, string, string, int, uint, uint, uint, string, string, string, string)>
        {
            [0] = (0x60400009, "dad248354d3256ca38916ac22a953f5dd16193e2795383aa41f5ad3ec7131e27", "df40e5387eeb7f55fe2cafb6954ace97b124f53415ed76d7032ed8b5feeabf2c",
                919, 823, 1419, 43648541, "59fb803cb2d98c6c3189e3ca0be8f4273170ac25c0a959b76b11e865d46b970e", "4369ed7b46e639a737d35a81fa42113172269207f73842e3f2ab11e7a96b10b5", "00", "4be70c355faf34b02c6dc23e9f54eab8f7c65f4c795af0aebbca4dc02c76183d"),
            [5] = (0x60ca0009, "fc48f8291e96149a438695cae7aaa36939aabd9002c4d64dcfb18939413bfa2d", "002a802ffc3ef1bc5558a314cb49f6285fac74b378afc8c7299350e7dc9f7d1b",
                956, 504, 1298, 43693671, "07459d6d81fa68323c7ec5052accacd53ae5775d9da03d61f543b0074efb7e43", "b5e11cea3f266f277ed8027269cf53ea57ad3454b8274d396281d56f6e921322", "0200", "458b2a35607a4a5e9e7f867adc999f68b97b75b6c73366da9b387b56204f7e23"),
            [9] = (0x60ca0009, "04493b058cfc8899bbede3d0d421991dcdc1acd44ddab20e9c5d7df4dc9bdf0c", "e3cf54b7039bef9ceaf09f672ce17e5c91a613c1cc8037c3f7496088b6594e6e",
                956, 504, 1298, 43693671, "07459d6d81fa68323c7ec5052accacd53ae5775d9da03d61f543b0074efb7e43", "b5e11cea3f266f277ed8027269cf53ea57ad3454b8274d396281d56f6e921322", "0200", "458b2a35607a4a5e9e7f867adc999f68b97b75b6c73366da9b387b56204f7e23"),
            [16] = (0x606a0004, "7c77d44b6fbb839225febebe16e8bde05b597dbc6209fb11a4f7715cedcc3123", "7502809b908bac103cce04bba3d7afcff919114c2693dd44d50fa460b1a81065",
                955, 571, 1316, 43693100, "a081ccd560a9b446f33883af0193cd47071c6fcd6b27892ca6f6f007802432cb", "c98b350f9d5e74e24391926a1c172bff8a515fab40d4f51b2915c0d36e4ec316", "0200", "a1f70f2fb7f07365808eb09a5c70f01a9351a97a13edb51022ffdb514425def3"),
        };

    internal static (string Policy, int View, int Group) SelectionPolicy(AreaVisualKey key, uint raw, AreaEntryIdentity entry, byte[] bytes,
        Objects.ObjectWireRecord record, int nominal, int group, int frame, int palette)
    {
        if (key.Kind != AreaSourceKind.Mobile || key.Layer != AreaLayerKind.Body || key.Sector.SourceSha256 != NormalGameMobileSha256)
            return ("strict-full-entry", bytes.Length, group);
        var sha = Objects.ObjectWireReader.Hash(bytes);
        if (TrailingBytes.TryGetValue(key.SourceIndex, out var pin) && key.ObjectIdSha256 == pin.Object)
        {
            if (record.Sha256 != pin.Record || raw != pin.Raw || entry.Source.Role != AreaSourceRole.Resource2 || entry.Ordinal != pin.Ordinal || entry.Flags != 2 ||
                entry.StoredBytes != pin.Stored || entry.DecodedBytes != pin.Decoded || entry.Offset != pin.Offset || entry.NameSha256 != pin.Name ||
                sha != pin.Sha || bytes.Length != pin.Decoded || nominal != 0 || group != 0 || frame != 0 || palette != 0)
                throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Frame));
            var view = bytes.Length - pin.Tail.Length / 2;
            if (Convert.ToHexString(bytes.AsSpan(view)).ToLowerInvariant() != pin.Tail || Objects.ObjectWireReader.Hash(bytes.AsSpan(0, view)) != pin.View)
                throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Frame));
            return ("arc-world-009-exact-art-view-v1", view, 0);
        }
        if (key.SourceIndex == 15 && key.ObjectIdSha256 == "01ad8798be58cfdc63c11670968b24e52230e728544285bd00f0dd3ae776f9fd")
        {
            if (record.Sha256 != "7731877218f9828782b9ad1add2876fcd52a45ad5993d990b92f62ad7606293a" || raw != 0x40a80b40 || entry.Source.Role != AreaSourceRole.Resource2 ||
                entry.Ordinal != 2816 || entry.Flags != 2 || entry.StoredBytes != 2885 || entry.DecodedBytes != 3122 || entry.Offset != 55379562 ||
                entry.NameSha256 != "2e6008afc41b62cef615fb868d38e642f59bf6d45060c35b4f80bdaca924838e" || sha != "3a8646d7fdb67b529f81a7cbd9b0b2644dc6367ca6025425521fb99b36c96d8f" ||
                nominal != 1 || group != 1 || frame != 0 || palette != 0 || (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 1) != 1)
                throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Frame));
            return ("arc-world-009-exact-single-group-alias-v1", bytes.Length, 0);
        }
        return ("strict-full-entry", bytes.Length, group);
    }

    internal static IReadOnlyList<AreaEndpoint> Endpoints(IEnumerable<AreaObjectRecord> objects)
    {
        var exit = objects.Single(o => o.Key.Kind == AreaSourceKind.Static && o.Key.Sector == Sector && o.Key.SourceIndex == 99);
        if (exit.Key.ObjectIdSha256 != "9d908ecfb6b256def8b49a7c504e6c889c4b0e41fe6ce3e01863dd7b61a20aa0" || exit.PrototypeNumber != 4047 || exit.WorldX != 2093 || exit.WorldY != 2019)
            throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Appearance));
        return [new(Sector, 99, 4047, 1, 1532, 5000, 1, 92913, 82598, exit.Key.ObjectIdSha256)];
    }
}
