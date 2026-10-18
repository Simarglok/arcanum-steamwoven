namespace Arcanum.Data.Area;

// ARC-WORLD-009: pinned original profile of the Crash Site cave, catalogue map 5078
// (runtime 79), from docs/research/cave-transition-evidence.md. Safe numeric
// identities only; no original payload, logical names or pixel corpus is embedded.
// ARC-DATA-036: the profile names its inputs and its expected outputs only; the owner builds it by the rules of every
// plan, and Check asserts that they reproduce the outputs.
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
        return new(CatalogueMap, RuntimeMap, new(32, 31), 141, "maps/Crash Site Cave", identities, mappings)
        {
            OriginalMap = OriginalAreaMap.CrashSiteCave, ResourceOrder = [new(new(AreaSourceRole.Resource2)), new(new(AreaSourceRole.Resource1))],
            Endpoints = Endpoints,
        };
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

    /// <summary>The expected use-click transition: the cave exit (ordinal 99 of the cave sector, prototype 4047), whose use
    /// script 1532 teleports to the main map (catalogue 5000, runtime 1) at (92913, 82598). ARC-DATA-036: the map's
    /// transition data give exactly this one (opt-in test).</summary>
    internal static readonly IReadOnlyList<AreaEndpoint> Endpoints =
        [new(Sector, 99, 4047, AreaEndpoints.UseSap, 1532, 5000, 1, 92913, 82598, "9d908ecfb6b256def8b49a7c504e6c889c4b0e41fe6ce3e01863dd7b61a20aa0")];
}
