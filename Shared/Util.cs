using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Shared
{
    public static class Util
    {
        public static string Hex(this Span<byte> span)
        {
            return span.ToArray().Hex();
        }

        public static string Hex(this IEnumerable<byte> array)
        {
            return string.Join(' ', array.ToArray().Select(x => x.ToString("X2")));
        }

        public static unsafe byte* Ptr(this Span<byte> span)
        {
            fixed (byte* data = span)
            {
                return data;
            }
        }

        public static string TrimNullTerm(this string text)
        {
            return text.TrimEnd('\0');
        }

        public static IMemoryOwner<byte> RentZero(this MemoryPool<byte> pool, int minSize)
        {
            IMemoryOwner<byte> owner = pool.Rent(minSize);
            CryptographicOperations.ZeroMemory(owner.Memory.Span);
            return owner;
        }

        public static readonly List<string> KingdomNames = new()
        {
            "Cap Kingdom",
            "Cascade Kingdom",
            "Sand Kingdom",
            "Wooded Kingdom",
            "Lake Kingdom",
            "Cloud Kingdom",
            "Lost Kingdom",
            "Metro Kingdom",
            "Seaside Kingdom",
            "Snow Kingdom",
            "Luncheon Kingdom",
            "Ruined Kingdom",
            "Bowser's Kingdom",
            "Moon Kingdom",
            "Mushroom Kingdom",
            "Dark Side",
            "Darker Side",
        };

        public static readonly Dictionary<string, string> CheckpointNames = new() { 
         {"obj153(SeaWorldUnderGlassZone[obj1898])", "Glass Palace"},
         {"obj1143", "Goomba Woods"},
         {"obj1455", "Yoshi's House"},
         {"obj1145", "Mushroom Pond"},
         {"obj162", "Peach's Castle Main Entrance"},
         {"obj345", "Ringing-Bells Plateau"},
         {"obj127", "Ever-After Hill"},
         {"obj1006", "Quiet Wall"},
         {"obj1348", "Wedding Hall"},
         {"obj2899", "Mountainside Platform"},
         {"obj543", "Swamp Hill"},
         {"obj545", "Rocky Mountain Summit"},
         {"obj1726", "Third Courtyard (Front)"},
         {"obj2697", "Island in the Sky"},
         {"obj1626", "Third Courtyard (Rear)"},
         {"obj2028", "Souvenir Shop"},
         {"obj182(SeaWorldLavaZone[obj1399])", "Hot Spring Island"},
         {"obj545(SkyWorldWallZone[obj2161])", "Second Courtyard"},
         {"obj59(SeaWorldDamageBallZone[obj1070])", "Above Rolling Canyon"},
         {"obj212(SeaWorldLighthouseZone[obj1402])", "Lighthouse"},
         {"obj5012", "Rooftop Garden"},
         {"obj5016", "New Donk City Hall Rooftop"},
         {"obj4904", "New Donk City Hall Plaza"},
         {"obj9017", "Construction Access"},
         {"obj14061", "Isolated Rooftop"},
         {"obj9086", "Construction Site"},
         {"obj3994", "Main Street Entrance"},
         {"obj3996", "Heliport"},
         {"obj3998", "Mayor Pauline Commemorative Park"},
         {"obj4000", "Outdoor Cafe"},
         {"obj8918", "City Outskirts"},
         {"obj381", "Top-Hat Tower"},
         {"obj2497", "Central Plaza"},
         {"obj1440", "Corner of the Freezing Sea"},
         {"obj1066", "Above the Ice Well"},
         {"obj1387", "Snow Kingdom Clifftop"},
         { "obj2832","Island in the Sky"},
         {"obj3139", "Top of the Big Stump"},
         {"obj1724", "Stone Bridge"},
         {"obj597", "Waterfall Basin"},
         {"obj601", "Fossil Falls Heights"},
         {"obj655", "Tostarena Town"},
         {"obj1511", "Moe-Eye Habitat"},
         {"obj98", "Tostarena Ruins Sand Pillar"},
         {"obj99", "Tostarena Ruins Entrance"},
         {"obj530", "Jaxi Ruins"},
         {"obj525", "Tostarena Ruins Round Tower"},
         {"obj5575", "Southwestern Floating Island"},
         {"obj1595", "Tostarena Northwest Reaches"},
         {"obj1597", "Desert Oasis"},
         {"obj1888(SkyWorldCastleZone[obj2160])", "Main Courtyard Entrance"},
         {"obj1890(SkyWorldCastleZone[obj2160])", "Outer Wall"},
         {"obj1891(SkyWorldCastleZone[obj2160])", "Beneath the Keep"},
         {"obj6423(SkyWorldCastleZone[obj2160])", "Showdown Arena"},
         {"obj2134(SkyWorldCastleZone[obj2160])", "Inner Wall"},
         {"obj1392(SkyWorldCastleZone[obj2160])", "Main Courtyard"},
         {"obj2264", "Ocean Trench West"},
         {"obj2266", "Ocean Trench East"},
         {"obj1866", "Rolling Canyon"},
         {"obj2621", "Diving Platform"},
         {"obj1524", "Beach House"},
         {"obj3734", "Sky Garden Tower"},
         {"obj1841", "Summit Path"},
         {"obj6865", "Iron Cage"},
         {"obj3708", "Iron Road: Entrance"},
         {"obj447", "Iron Road: Halfway Point"},
         {"obj2821", "Secret Flower Field Entrance"},
         {"obj3835", "Observation Deck"},
         {"obj7333", "Iron Mountain Path, Station 8"},
         {"obj5216", "Forest Charging Station"},
         {"obj583(LakeWorldTownZone[obj324])", "Courtyard"},
         {"obj693(LakeWorldTownZone[obj324])", "Water Plaza Entrance"},
         {"obj220(LakeWorldTownZone[obj324])", "Water Plaza Terrace"},
         {"obj1389(LakeWorldTownZone[obj324])", "Water Plaza Display Window"},
         {"obj839(LakeWorldTownZone[obj324])", "Underwater Entrance"},
         {"obj1323(LakeWorldTownZone[obj324])", "Viewing Balcony"},
         {"obj4117", "Top of the Peak Climb"},
         {"obj4074", "Path to the Meat Plateau"},
         {"obj4619", "Salt-Pile Isle"},
         {"obj642", "Peronza Plaza"},
         {"obj2549", "Volcano Cave Entrance"},
         {"obj6042", "Floating Sky Island"},
         {"obj4120", "Remote Island in the Lava"},
         {"obj1061", "Meat Plateau"}, 
         {"obj2292", "Start of the Peak Climb"},
        };
    }
}

namespace Sever
{
    namespace Server
    {
        public enum FlipOptions {
            Both,
            Self,
            Others
        }
        public record Time(ushort Minutes, byte Seconds);
    }
}