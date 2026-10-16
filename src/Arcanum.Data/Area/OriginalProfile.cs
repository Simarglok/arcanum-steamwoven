using System.Buffers.Binary;
using Arcanum.Data.Objects;

namespace Arcanum.Data.Area;

// Safe numeric identities from the independently verified source inventory.
// No original payload, logical resource names or pixel corpus is embedded.
internal static class OriginalProfile
{
    internal static readonly IReadOnlyDictionary<int, (long Length, string Sha256)> Prototypes = new Dictionary<int, (long, string)>
    {
        [4026] = new(414, "1378ddb05e981098eca7816b22a366e6733c9df7c580ccc3f19345f782319874"),
        [4030] = new(406, "f15e7b0ed999ac9eeabcafa1bcddbd55b96dcbba9b2d71a7d485024c06f9c310"),
        [4042] = new(378, "3937f177ce5cbe07cf2ec16925b60fdb5984025ee827d174944cefc071329c9f"),
        [4027] = new(414, "8de2fddd8d4b2b8e38ad31337a7200b9cc4dd88b5e2ce2a4080144d89111bb80"),
        [4038] = new(414, "2531e1a1c6ddca2f483a2d557bebcfd6c1b0b89bd2d996a3f559adc28924214b"),
        [4028] = new(414, "c2442deca958715bc49f5f0504066f951a7d549e5787be41dcded2257e23d6e3"),
        [4047] = new(422, "92d36db76ba575fd8377a2dc716f2af1b8e00ce343a3bf7dc866cfdc86bffd3d"),
        [4033] = new(418, "a71c92deaa473f2ddf137a7bccdee1fa9055a0ca442d6219cf5a305e53b1f5e3"),
        [4031] = new(414, "3c5ad2f204a010b3908d0a02e693128e9f8ff421b4a7dafb7ee3e78b66420c53"),
        [4048] = new(378, "ec807b77ea283d8d84387993a439953b7acf1a1ef7bdfb30b6a86a334dcfe8d1"),
        [4041] = new(378, "a681158b72ecb234e55afdef344d3ae34648b7fde939c8d4ee6ef2320ab37ddb"),
        [4037] = new(414, "70d70e48d45bf1d9f3678dc6b0315f49fd02200fc3cb4c8e1086702759c891ff"),
        [4036] = new(418, "9508639c13871738a9794b7c576db677bc5dacd83c74db51d5ba50a059db8819"),
        [4034] = new(418, "30349db883c54d52df0fed73a7cbb5793190d9382add3b4e8db23b8a1da82df9"),
        [4035] = new(418, "5ab0353bb71a4b61b24bce9e1ed8eef7f1a29b9a3db286b9b3fa7f5ec4a50c62"),
        [6198] = new(599, "ad12116f0daef5d8b16fd0e40429a03bd7dbbb86bd5cc0e99399e46e2fb6b272"),
        [17132] = new(952, "15fad91f2d187151ce454648494e8809dcb1721dffd6bf017496024fb5990be6"),
        [17261] = new(960, "3980b8e4eb6d461981a3a7570d47903ce48d733f869e4bfe2eff70c58985b4a0"),
        [28359] = new(920, "68aef406217aa317f285580e970bd2e78b2733ea520401d7161607c0ea3bf82e"),
        [27356] = new(964, "7a8837a44b87b4578e3b96426340376dc2d0d1aa68acf11032ef2cbda1fbe9c4"),
        [17144] = new(952, "f10d0791e9265bf6ae69d9a2a1543f5f4fffc61f73c4a53c12fbf888aa297e43"),
        [28447] = new(964, "760942620b2238395e9e95a5b831063b30f0ef4b7343735fbf75a835a1a5f0aa"),
        [8074] = new(514, "d09c6b407455af6cb9c1d922dfda7bdb49bf1989229e071407ec01522a250496"),
        [17145] = new(960, "3069203939c1d3bcd7da02421da8478975ca0aaee73045d4c509668a98de5a4e"),
        [17188] = new(952, "cf0fc991f2ffbc36a24e67f71d140e9df02acd42f3d4f837098481995b543b78"),
        [6057] = new(607, "f45adc2669e323c25dd8415e22a440ea4840e0ef4c7ff4b3cbac95158e6dcc50"),
        [8179] = new(550, "7930e66c960644e3aa1d2f9792cc6061bc50ff7593c0eeacc4923c5b2dbdcd07"),
        [17099] = new(960, "56691b1bdfee2010a35030f80a8727f471791c86de67e47b4d13e3b29a010a51"),
        [17232] = new(952, "596fdf4f2d173dd6dd78bb19a59fc8a2ff80bab368c4278ac8a4ea4200f003ee"),
        [17102] = new(952, "084a85124a37c41097806dface638254194efd46a5b318009e1c841c42797ea4"),
        [8114] = new(518, "8351a19f6dbe0ea0f36142515b63ea76dad21f20d87c6abae05ad932b0ddd80b"),
        [15085] = new(464, "84a26168b6136ef5b824ca13316fd006ecfae8e1cbdd13b488d26b99e90a162c"),
        [14083] = new(476, "147496b893eb643022a61f5cd7511cd9682f53fa5caa00336e4cd8fe53f37a71"),
        [3055] = new(398, "c8ad97824c3d3d4c8d5e9f5f2d814965aa9dda8066c17e770b8589e45ef41dab"),
        [17088] = new(952, "fb8f9de2cb80d6d3234e4fa2e533aa33fc1b1fdbd5ef500e23be2710a9a9a803"),
        [9076] = new(468, "fb319ab91ae047c255fce732342b180112b7bff55d364d8ff71713b844996393"),
        [10081] = new(464, "5393b45420b6048b7a20e05378e33d33fe5964e78b8fd06a450f8ab29e51f73b"),
        [10082] = new(464, "d12cd58751f78cd7308493dcf72656c92bfcf370a977700b5cde779da8968a91"),
        [15102] = new(464, "822d1f390784b302ab42f2983685b4da0a33c22865f4b5a6ea4a5f55b94cd15d"),
        [15107] = new(464, "ebd64f02884ccde7bbf8eef29c7d3e94faf22161c9673605cf0f0ea3204d27cc"),
        [15109] = new(464, "5dc265008d13b61b30f7d26e3a7744977f3d9a85908b14cc43e048b6f283fc9d"),
        [10086] = new(464, "341212b00417295c17215f544e26360a2c960bb47dda0fbeacba1ffb656b8c18"),
        [15089] = new(464, "2c77cdea0500e07ceea4845080d026b6f3c30eaf9cc3dc99889c38c7d1579b17"),
        [15091] = new(464, "0159bcece3d4fc20e1e772a16b9a87315f5d21808246de9da342b2be6eeefe5e"),
        [10113] = new(464, "ba88be337afdedb9a60d02cf79e3e68002f7f6ef530e0b75b1206bd2fbd69669"),
        [17256] = new(952, "23388a0c3afa404ccf63e26b6a8616a49c8baba79d240fa5d497418055d327e3"),
        [17198] = new(952, "1ef87125af320b39e119b6432a2c0921c06a1245d19e6fd521055958b73c5b54"),
        [17100] = new(952, "a2503ecd695fdaa59225270915fee348093d4c3437198385a80a7790df084cb7"),
        [17233] = new(960, "c11245979e6b10ebd41d81b536ba56551a2354aab47ffaa1d46602b33ee107bd"),
        [17165] = new(960, "5dbd50445b36d227344aed9fe58290fe3bcab5a697611f8b01a4700ada809b9b"),
        [28339] = new(920, "086ccf11d10f045488ec248dd92e4d42909c02fbdb1a3ac9965ea6ab5e9df776"),
        [17124] = new(952, "81a4e3657456d36a478062b7c16e28fac5e8eb7c0c56003cd9cfe668d81604a2"),
        [27359] = new(964, "6c79ed375bc04179dc4578933c92645d03f22a7fa44bce6d069ac445e314d4d3"),
        [3067] = new(398, "95a75820057c6f664bd1e6b0bc527cab4bb82723b78c1db23ddd98ee580b2c6e"),
        [17092] = new(952, "6e3ad2cb6669eb9deeee212e55110b37c9311f4dbdb47b88a199e6313d7702d4"),
        [17212] = new(952, "47c7bfd9574b563ba9ab988ba7c3f94472411b4fa755c29570963d2bd25e9806")
    };
    internal static AreaSourcePlan Plan()
    {
        var identities = new List<AreaInputIdentity>
        {
            new(new(AreaSourceRole.Module), 355938305, "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f"),
            new(new(AreaSourceRole.Resource1), 447470816, "6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576"),
            new(new(AreaSourceRole.Resource2), 147991122, "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d"),
            new(new(AreaSourceRole.NormalGameMobile), 1823280, "a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5")
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
            Map(AreaMappingRole.MissingPolicy, AreaSourceRole.Resource1, 4695, 108, 332, 278084522, "90de5bb07348cecfc2bc60cfb68d8b56493a8f36ae906da63d1ec6499f84b3a6", "49502ba4e4236f7c7835e697c3ff569f894f2814f82cd3543ce861c28526b7e0")
        };
        return new(5000, 1, new(1452, 1290), 2, "maps/Arcanum1-024-fixed", identities, mappings) { OriginalMap = OriginalAreaMap.CrashSiteExterior };
    }

    internal static void Check(AreaReconciliation counts, IEnumerable<AreaSectorRecord> sectors, IEnumerable<AreaObjectRecord> objects, IEnumerable<AreaVisualRecord> visuals)
    {
        if (counts != new AreaReconciliation(621, 9, 11, 5, 36864, 45056, 426, 7399, 88, 10, 7301, 0, 7399, 56, 2444))
            throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Publication));
        var core = sectors.Where(s => s.Scope == AreaScope.Core).ToArray();
        foreach (var x in new long[] { 1451, 1452, 1453 }) foreach (var y in new long[] { 1289, 1290, 1291 })
            if (core.Count(s => s.Coordinate == new AreaSectorCoordinate(x, y)) != 1) throw new AreaException(new(AreaErrorCode.InvalidSelection, AreaStage.Selection));
        if (objects.Any(o => !o.SchemaReady || !o.PlacementReady || !o.RequiredLayersReady) || visuals.Count(v => v.Key.Kind == AreaSourceKind.Mobile) != 88)
            throw new AreaException(new(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Publication));
    }
    internal static IReadOnlyList<AreaEndpoint> Endpoints(IEnumerable<AreaObjectRecord> objects)
    {
        var endpoint = objects.Single(o => o.Key.Kind == AreaSourceKind.Static && o.Key.Sector == 86570436011 && o.Key.SourceIndex == 7);
        if (endpoint.Key.ObjectIdSha256 != "9d908ecfb6b256def8b49a7c504e6c889c4b0e41fe6ce3e01863dd7b61a20aa0" || endpoint.PrototypeNumber != 4047)
            throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Appearance));
        return [new(86570436011, 7, 4047, 1, 2793, 5078, 79, 2087, 2018, endpoint.Key.ObjectIdSha256)];
    }
    internal static (string Policy, int View, int Group) SelectionPolicy(AreaVisualKey key, uint raw, AreaEntryIdentity entry, byte[] bytes,
        ObjectWireRecord record, int nominal, int group, int frame, int palette)
    {
        if (key.Kind != AreaSourceKind.Mobile || key.Layer != AreaLayerKind.Body) return ("strict-full-entry", bytes.Length, group);
        var sha = ObjectWireReader.Hash(bytes);
        if (key.SourceIndex is 4704 or 4705 or 5822 or 5823)
        {
            var (id, recordSha) = key.SourceIndex switch
            {
                4704 => ("27a6d09e145a3efb4a233f393a1e66dfe8d3690963b148a452a636bd1f8e17bf", "a87754b94f70f40dc2e14d6eebd6ea0795b3383ad64241f58669e9ee7466e028"),
                4705 => ("3239ae11d3124ee747c827aec92e366990b8dfbacbf2118222ebaac229bb8b44", "e3a7fabb7bb6a426c06a6583ddcb1e8bf6aef0f291ff935441eb3b72058a1a61"),
                5822 => ("1e2cfbecc847a79219da5c361cd36046447512f50cde7a56228976828a114924", "1c5f5e0b9ae21bd4c3baa7d017e925f7f7d1df067d4bd45ad1b6ef6170e7485e"),
                _ => ("9f985ee57423c72d95219cf627e07149334af59ed3cb604eb7ac4dda45967a3d", "ca55aac9a234a222c9e27d2823c080ed03a939f3b722470c789c4f7773ce120d")
            };
            if (key.Sector.SourceSha256 != "a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5" || key.ObjectIdSha256 != id || record.Sha256 != recordSha || raw != 1614807049 ||
                entry.Source.Role != AreaSourceRole.Resource2 || entry.Ordinal != 919 || entry.StoredBytes != 823 || entry.DecodedBytes != 1419 || entry.Offset != 43648541 || entry.Flags != 2 ||
                entry.NameSha256 != "59fb803cb2d98c6c3189e3ca0be8f4273170ac25c0a959b76b11e865d46b970e" || sha != "4369ed7b46e639a737d35a81fa42113172269207f73842e3f2ab11e7a96b10b5" || bytes.Length != 1419 || bytes[^1] != 0 || nominal != 0 || group != 0 || frame != 0 || palette != 0 ||
                ObjectWireReader.Hash(bytes.AsSpan(0, 1418)) != "4be70c355faf34b02c6dc23e9f54eab8f7c65f4c795af0aebbca4dc02c76183d")
                throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Frame));
            return ("arc-data-009-exact-art-view-v1", 1418, 0);
        }
        if (key.SourceIndex == 5699)
        {
            if (key.Sector.SourceSha256 != "a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5" || key.ObjectIdSha256 != "49fd9353065e5cf48d7e8a086ddfaedcafb221fd84b6cb2d59a840fa87eca395" || record.Sha256 != "77e674884f8361e10d625cdfbbeae6e176baf731fc329e6d803e9fa144fcb99c" || raw != 3494914496 ||
                entry.Source.Role != AreaSourceRole.Resource1 || entry.Ordinal != 5445 || entry.Flags != 2 || entry.StoredBytes != 1724 || entry.DecodedBytes != 2015 || entry.Offset != 376688235 ||
                entry.NameSha256 != "aca95bdca2f75f25ccf73adf976b0ba7c7721b467d54f879b90e27474fd26902" || sha != "d91c5cb045ae8f24896fad511d000aa8a35d8f8c5b88df6566199b2249921281" || nominal != 5 || group != 5 || frame != 0 || palette != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 1)
                throw new AreaException(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Frame));
            return ("arc-data-009-exact-single-group-alias-v1", bytes.Length, 0);
        }
        return ("strict-full-entry", bytes.Length, group);
    }
}
