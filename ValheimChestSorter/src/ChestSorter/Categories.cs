using System.Collections.Generic;

namespace ChestSorter
{
    /// <summary>One shelf of the storage room: a kind of goods that gets its own chests.</summary>
    internal class Category
    {
        public readonly string Id;
        public readonly string LabelEn;
        public readonly string LabelRu;

        /// <summary>Where this category is poured into when there are not enough chests for all of them.</summary>
        public readonly string FallbackId;

        public readonly int Order;

        public Category(string id, string labelRu, string labelEn, string fallbackId, int order)
        {
            Id = id;
            LabelRu = labelRu;
            LabelEn = labelEn;
            FallbackId = fallbackId;
            Order = order;
        }

        public string Label
        {
            get { return Util.IsRussian ? LabelRu : LabelEn; }
        }
    }

    internal static class Categories
    {
        public const string Food = "FOOD";
        public const string Potion = "POTION";
        public const string Weapon = "WEAPON";
        public const string Armor = "ARMOR";
        public const string Ammo = "AMMO";
        public const string Tool = "TOOL";
        public const string Metal = "METAL";
        public const string Wood = "WOOD";
        public const string Hide = "HIDE";
        public const string Monster = "MONSTER";
        public const string Seed = "SEED";
        public const string Valuable = "VALUABLE";
        public const string Enchant = "ENCHANT";
        public const string MagicItem = "MAGICITEM";
        public const string Misc = "MISC";

        private static readonly List<Category> All = new List<Category>
        {
            new Category(Food,      "ЕДА",                "FOOD",            Misc,   10),
            new Category(Potion,    "МЕДЫ И ЗЕЛЬЯ",       "MEADS",           Food,   20),
            new Category(Weapon,    "ОРУЖИЕ И ЩИТЫ",      "WEAPONS",         Misc,   30),
            new Category(Armor,     "БРОНЯ",              "ARMOR",           Weapon, 40),
            new Category(MagicItem, "МАГИЧЕСКИЕ ВЕЩИ",    "MAGIC GEAR",      Weapon, 45),
            new Category(Ammo,      "БОЕПРИПАСЫ",         "AMMO",            Weapon, 50),
            new Category(Tool,      "ИНСТРУМЕНТЫ",        "TOOLS",           Weapon, 60),
            new Category(Metal,     "МЕТАЛЛЫ И РУДА",     "METALS AND ORE",  Misc,   70),
            new Category(Wood,      "ДЕРЕВО И КАМЕНЬ",    "WOOD AND STONE",  Misc,   80),
            new Category(Hide,      "ШКУРЫ И ТКАНЬ",      "HIDES AND CLOTH", Monster, 90),
            new Category(Monster,   "ЧАСТИ СУЩЕСТВ",      "CREATURE PARTS",  Misc,  100),
            new Category(Seed,      "СЕМЕНА И УРОЖАЙ",    "SEEDS AND CROPS", Misc,  110),
            new Category(Valuable,  "ЦЕННОСТИ",           "VALUABLES",       Misc,  120),
            new Category(Enchant,   "ЗАЧАРОВАНИЕ",        "ENCHANTING",      Misc,  130),
            new Category(Trophy,    "ТРОФЕИ",             "TROPHIES",        Misc,  140),
            new Category(Misc,      "РАЗНОЕ",             "MISC",            null,  999)
        };

        public const string Trophy = "TROPHY";

        private static readonly Dictionary<string, Category> ById = Build();

        private static Dictionary<string, Category> Build()
        {
            Dictionary<string, Category> map = new Dictionary<string, Category>();
            for (int i = 0; i < All.Count; i++)
            {
                map[All[i].Id] = All[i];
            }

            return map;
        }

        public static Category Get(string id)
        {
            Category category;
            if (id != null && ById.TryGetValue(id, out category))
            {
                return category;
            }

            return ById[Misc];
        }

        public static IEnumerable<Category> Enumerate()
        {
            return All;
        }

        public static bool Exists(string id)
        {
            return id != null && ById.ContainsKey(id);
        }
    }
}
