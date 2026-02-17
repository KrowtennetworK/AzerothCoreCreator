using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MySqlConnector;
using System.Diagnostics;


namespace AzerothCoreCreator
{
    public partial class MainWindow : Window
    {
        private bool _connected = false;
        private string _lastSql = "";

        // checkbox -> bit value maps
        private Dictionary<CheckBox, uint> _npcFlags = new Dictionary<CheckBox, uint>();
        private Dictionary<CheckBox, uint> _unitFlags = new Dictionary<CheckBox, uint>();
        private Dictionary<CheckBox, uint> _unitFlags2 = new Dictionary<CheckBox, uint>();
        private Dictionary<CheckBox, uint> _extraFlags = new Dictionary<CheckBox, uint>();

        // speech lines
        private class SpeechLine
        {
            public int GroupId;
            public string Type; // SAY/YELL/WHISPER
            public string Text;
            public int Sound;
            public int Emote;

            public override string ToString()
            {
                return string.Format("[Group {0}] {1}: {2} (sound:{3} emote:{4})", GroupId, Type, Text, Sound, Emote);
            }

        }

        private readonly List<SpeechLine> _speechLines = new List<SpeechLine>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateCreatureCombos();
            PopulateItemCombos();
            PopulateQuestDefaults();
            UpdateQuestPreview();
            BuildFlagCheckboxes();
            SetDisconnectedUI("Ready.");
            BuildFlagCheckboxes();
            SetDisconnectedUI("Ready.");
            InitItemClassAndSubclasses();


            // Item tab extras (safe defaults)
            ItemOptionToggle_Changed(null, null);
            UpdateItemAllowableClassMask();
            UpdateItemAllowableRaceMask();
            UpdateItemPreview();

        }

        // ===================== CONNECTION =====================

        private string BuildConnString()
        {
            string host = HostBox.Text.Trim();
            string port = PortBox.Text.Trim();
            string user = UserBox.Text.Trim();
            string pass = PassBox.Password;
            string db = WorldDbBox.Text.Trim();

            int p = 3306;
            int.TryParse(port, out p);

            var csb = new MySqlConnectionStringBuilder();
            csb.Server = host;
            csb.Port = (uint)p;
            csb.UserID = user;
            csb.Password = pass;
            csb.Database = db;

            // important for multi-insert scripts
            csb.AllowUserVariables = true;
            csb.AllowLoadLocalInfile = false;

            return csb.ConnectionString;
        }

        private void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var conn = new MySqlConnection(BuildConnString()))
                {
                    conn.Open();
                }

                SetConnectedUI("Connected (test OK).");
            }
            catch (Exception ex)
            {
                SetDisconnectedUI("Connection failed: " + ex.Message);
            }
        }

        private void ConnectAndLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var conn = new MySqlConnection(BuildConnString()))
                {
                    conn.Open();

                    long nextCreature = GetNextId(conn, "creature_template", "entry");
                    long nextItem = GetNextId(conn, "item_template", "entry");
                    long nextQuest = GetNextId(conn, "quest_template", "ID");

                    CreatureEntryBox.Text = nextCreature.ToString(CultureInfo.InvariantCulture);
                    ItemEntryBox.Text = nextItem.ToString(CultureInfo.InvariantCulture);
                    QuestIdBox.Text = nextQuest.ToString(CultureInfo.InvariantCulture);

                    CreatureNextIdHint.Text = "(auto: MAX(entry)+1)";
                    ItemNextIdHint.Text = "(auto: MAX(entry)+1)";
                    QuestNextIdHint.Text = "(auto: MAX(ID)+1)";
                }

                SetConnectedUI("Connected + loaded next IDs.");
            }
            catch (Exception ex)
            {
                SetDisconnectedUI("Connect/Load failed: " + ex.Message);
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)

        {
            SetDisconnectedUI("Disconnected.");
        }

        private void SetConnectedUI(string msg)
        {
            _connected = true;
            ConnLight.Fill = Brushes.LimeGreen;
            ConnStatusText.Text = "Connected";
            ConnEndpointText.Text = string.Format("({0}:{1})", HostBox.Text.Trim(), PortBox.Text.Trim());
            ConnMessage.Text = msg;
        }

        private void SetDisconnectedUI(string msg)
        {
            _connected = false;
            ConnLight.Fill = Brushes.Gray;
            ConnStatusText.Text = "Not connected";
            ConnEndpointText.Text = "";
            ConnMessage.Text = msg;
        }

        private long GetNextId(MySqlConnection conn, string table, string idCol)
        {
            string sql = string.Format("SELECT IFNULL(MAX({0}),0)+1 FROM `{1}`;", idCol, table);
            using (var cmd = new MySqlCommand(sql, conn))
            {
                object o = cmd.ExecuteScalar();
                long v = 1;
                if (o != null && o != DBNull.Value) long.TryParse(o.ToString(), out v);
                if (v < 1) v = 1;
                return v;
            }
        }

        // ===================== UI DATA =====================

        private void PopulateCreatureCombos()
        {
            // Factions (easy presets) – these are “faction IDs”, not relationships.
            // We’ll start with common ones; we can expand later.
            CreatureFactionCombo.Items.Clear();
            CreatureFactionCombo.Items.Add(new ComboBoxItem { Content = "Friendly (35)", Tag = 35 });
            CreatureFactionCombo.Items.Add(new ComboBoxItem { Content = "Neutral (7)", Tag = 7 });
            CreatureFactionCombo.Items.Add(new ComboBoxItem { Content = "Hostile (14)", Tag = 14 });
            CreatureFactionCombo.Items.Add(new ComboBoxItem { Content = "Stormwind (12)", Tag = 12 });
            CreatureFactionCombo.Items.Add(new ComboBoxItem { Content = "Orgrimmar (29)", Tag = 29 });
            CreatureFactionCombo.Items.Add(new ComboBoxItem { Content = "Booty Bay (21)", Tag = 21 });
            CreatureFactionCombo.SelectedIndex = 0;

            // Creature type (CreatureType)
            CreatureTypeCombo.Items.Clear();
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "None (0)", Tag = 0 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Beast (1)", Tag = 1 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Dragonkin (2)", Tag = 2 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Demon (3)", Tag = 3 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Elemental (4)", Tag = 4 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Giant (5)", Tag = 5 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Undead (6)", Tag = 6 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Humanoid (7)", Tag = 7 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Critter (8)", Tag = 8 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Mechanical (9)", Tag = 9 });
            CreatureTypeCombo.Items.Add(new ComboBoxItem { Content = "Not specified (10)", Tag = 10 });
            CreatureTypeCombo.SelectedIndex = 0;

            // Family (CreatureFamily) – only meaningful mostly for Beast type
            CreatureFamilyCombo.Items.Clear();
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "None (0)", Tag = 0 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Wolf (1)", Tag = 1 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Cat (2)", Tag = 2 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Spider (3)", Tag = 3 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Bear (4)", Tag = 4 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Boar (5)", Tag = 5 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Crocolisk (6)", Tag = 6 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Carrion Bird (7)", Tag = 7 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Crab (8)", Tag = 8 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Gorilla (9)", Tag = 9 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Raptor (11)", Tag = 11 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Tallstrider (12)", Tag = 12 });
            CreatureFamilyCombo.Items.Add(new ComboBoxItem { Content = "Turtle (20)", Tag = 20 });
            CreatureFamilyCombo.SelectedIndex = 0;
        }

        private void PopulateItemCombos()
        {
            ItemQualityBox.Items.Clear();
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Poor (0)", Tag = 0 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Common (1)", Tag = 1 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Uncommon (2)", Tag = 2 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Rare (3)", Tag = 3 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Epic (4)", Tag = 4 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Legendary (5)", Tag = 5 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Artifact (6)", Tag = 6 });
            ItemQualityBox.SelectedIndex = 1;

            ItemClassBox.Items.Clear();
            ItemClassBox.Items.Add(new ComboBoxItem { Content = "Armor (4)", Tag = 4 });
            ItemClassBox.Items.Add(new ComboBoxItem { Content = "Weapon (2)", Tag = 2 });
            ItemClassBox.Items.Add(new ComboBoxItem { Content = "Consumable (0)", Tag = 0 });
            ItemClassBox.Items.Add(new ComboBoxItem { Content = "Misc (15)", Tag = 15 });
            ItemClassBox.SelectedIndex = 0;

            // We keep subclass simple at first (we can make it dynamic based on class later)
            ItemSubclassBox.Items.Clear();
            ItemSubclassBox.Items.Add(new ComboBoxItem { Content = "Cloth (1)", Tag = 1 });
            ItemSubclassBox.Items.Add(new ComboBoxItem { Content = "Leather (2)", Tag = 2 });
            ItemSubclassBox.Items.Add(new ComboBoxItem { Content = "Mail (3)", Tag = 3 });
            ItemSubclassBox.Items.Add(new ComboBoxItem { Content = "Plate (4)", Tag = 4 });
            ItemSubclassBox.Items.Add(new ComboBoxItem { Content = "Shield (6)", Tag = 6 });
            ItemSubclassBox.SelectedIndex = 0;

            ItemInventoryTypeBox.Items.Clear();
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Head (1)", Tag = 1 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Neck (2)", Tag = 2 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Shoulder (3)", Tag = 3 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Chest (5)", Tag = 5 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Waist (6)", Tag = 6 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Legs (7)", Tag = 7 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Feet (8)", Tag = 8 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Wrist (9)", Tag = 9 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Hands (10)", Tag = 10 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Finger (11)", Tag = 11 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Trinket (12)", Tag = 12 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "One-Hand (13)", Tag = 13 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Two-Hand (17)", Tag = 17 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Off-Hand (23)", Tag = 23 });
            ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = "Back (16)", Tag = 16 });
            ItemInventoryTypeBox.SelectedIndex = 0;

            ItemBondingBox.Items.Clear();
            ItemBondingBox.Items.Add(new ComboBoxItem { Content = "None (0)", Tag = 0 });
            ItemBondingBox.Items.Add(new ComboBoxItem { Content = "Bind on Pickup (1)", Tag = 1 });
            ItemBondingBox.Items.Add(new ComboBoxItem { Content = "Bind on Equip (2)", Tag = 2 });
            ItemBondingBox.Items.Add(new ComboBoxItem { Content = "Bind on Use (3)", Tag = 3 });
            ItemBondingBox.SelectedIndex = 0;
        }

        private void PopulateQuestDefaults()
        {
            QuestMinLevelBox.Text = "1";
            QuestLevelBox.Text = "1";
            QuestRewardXpBox.Text = "0";
            QuestRewardMoneyBox.Text = "0";
            QuestTypeBox.Text = "0";
            QuestSortBox.Text = "0";
        }

        private void BuildFlagCheckboxes()
        {
            // NPCFlag (common ones)
            NpcFlagWrap.Children.Clear();
            _npcFlags.Clear();
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Gossip", 1u);
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Questgiver", 2u);
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Vendor", 128u);
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Repair", 16384u);
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Trainer", 16u);
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Innkeeper", 65536u);
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Banker", 131072u);
            AddFlagCheck(NpcFlagWrap, _npcFlags, "Flightmaster", 8192u);

            // UnitFlags (starter)
            UnitFlagsWrap.Children.Clear();
            _unitFlags.Clear();
            AddFlagCheck(UnitFlagsWrap, _unitFlags, "Non-attackable", 2u);
            AddFlagCheck(UnitFlagsWrap, _unitFlags, "Pacified", 131072u);
            AddFlagCheck(UnitFlagsWrap, _unitFlags, "Not selectable", 33554432u);

            // UnitFlags2 (starter)
            UnitFlags2Wrap.Children.Clear();
            _unitFlags2.Clear();
            AddFlagCheck(UnitFlags2Wrap, _unitFlags2, "Feign Death", 1u);

            // ExtraFlags (starter)
            FlagsExtraWrap.Children.Clear();
            _extraFlags.Clear();
            AddFlagCheck(FlagsExtraWrap, _extraFlags, "No XP", 64u);
            AddFlagCheck(FlagsExtraWrap, _extraFlags, "No Loot", 1024u);
        }

        private void AddFlagCheck(Panel panel, Dictionary<CheckBox, uint> map, string label, uint bit)
        {
            var cb = new CheckBox();
            cb.Content = label;
            cb.Margin = new Thickness(0, 0, 10, 6);
            panel.Children.Add(cb);
            map[cb] = bit;
        }

        private uint SumFlags(Dictionary<CheckBox, uint> map)
        {
            uint v = 0;
            foreach (var kv in map)
            {
                if (kv.Key.IsChecked == true) v |= kv.Value;
            }
            return v;
        }

        private int ComboTagInt(ComboBox cb)
        {
            var item = cb.SelectedItem as ComboBoxItem;
            if (item == null) return 0;
            object tag = item.Tag;
            if (tag == null) return 0;
            int v = 0;
            int.TryParse(tag.ToString(), out v);
            return v;
        }
        // ===================== ITEM CLASS + SUBCLASS TABLES =====================

        private readonly Dictionary<int, string> _itemClassNames = new()
{
    { 0, "Consumable" },
    { 1, "Container" },
    { 2, "Weapon" },
    { 3, "Gem" },
    { 4, "Armor" },
    { 5, "Reagent" },
    { 6, "Projectile" },
    { 7, "Trade Goods" },
    { 8, "Generic" },
    { 9, "Recipe" },
    { 10, "Money" },
    { 11, "Quiver" },
    { 12, "Quest" },
    { 13, "Key" },
    { 14, "Permanent" },
    { 15, "Miscellaneous" },
    { 16, "Glyph" },
};

        private readonly Dictionary<int, List<KeyValuePair<int, string>>> _itemSubclassesByClass = new()
{
    // 0 = Consumable
    { 0, new List<KeyValuePair<int, string>> {
        new(0, "Consumable"),
        new(1, "Potion"),
        new(2, "Elixir"),
        new(3, "Flask"),
        new(4, "Scroll"),
        new(5, "Food & Drink"),
        new(6, "Item Enhancement"),
        new(7, "Bandage"),
        new(8, "Other"),
    }},

    // 1 = Container
    { 1, new List<KeyValuePair<int, string>> {
        new(0, "Bag"),
        new(1, "Soul Bag"),
        new(2, "Herb Bag"),
        new(3, "Enchanting Bag"),
        new(4, "Engineering Bag"),
        new(5, "Gem Bag"),
        new(6, "Mining Bag"),
        new(7, "Leatherworking Bag"),
        new(8, "Inscription Bag"),
    }},

    // 2 = Weapon
    { 2, new List<KeyValuePair<int, string>> {
        new(0, "1h Axe"),
        new(1, "2h Axe"),
        new(2, "Bow"),
        new(3, "Gun"),
        new(4, "1h Mace"),
        new(5, "2h Mace"),
        new(6, "Polearm"),
        new(7, "1h Sword"),
        new(8, "2h Sword"),
        new(10, "Staff"),
        new(13, "Fist Weapon"),
        new(14, "Miscellaneous"),
        new(15, "Dagger"),
        new(16, "Thrown"),
        new(18, "Crossbow"),
        new(19, "Wand"),
        new(20, "Fishing Pole"),
    }},

    // 3 = Gem
    { 3, new List<KeyValuePair<int, string>> {
        new(0, "Red"),
        new(1, "Blue"),
        new(2, "Yellow"),
        new(3, "Purple"),
        new(4, "Green"),
        new(5, "Orange"),
        new(6, "Meta"),
        new(7, "Simple"),
        new(8, "Prismatic"),
    }},

    // 4 = Armor
    { 4, new List<KeyValuePair<int, string>> {
        new(0, "Miscellaneous"),
        new(1, "Cloth"),
        new(2, "Leather"),
        new(3, "Mail"),
        new(4, "Plate"),
        new(6, "Shield"),
        new(7, "Libram"),
        new(8, "Idol"),
        new(9, "Totem"),
        new(10, "Sigil"),
    }},

    // 5 = Reagent
    { 5, new List<KeyValuePair<int, string>> {
        new(0, "Reagent"),
    }},

    // 6 = Projectile (WotLK uses 2/3 for arrow/bullet)
    { 6, new List<KeyValuePair<int, string>> {
        new(2, "Arrow"),
        new(3, "Bullet"),
    }},

    // 7 = Trade Goods
    { 7, new List<KeyValuePair<int, string>> {
        new(0, "Trade Goods"),
        new(1, "Parts"),
        new(2, "Explosives"),
        new(3, "Devices"),
        new(4, "Jewelcrafting"),
        new(5, "Cloth"),
        new(6, "Leather"),
        new(7, "Metal & Stone"),
        new(8, "Meat"),
        new(9, "Herb"),
        new(10, "Elemental"),
        new(11, "Other"),
        new(12, "Enchanting"),
        new(13, "Materials"),
        new(14, "Armor Enchantment"),
        new(15, "Weapon Enchantment"),
    }},

    // 8 = Generic
    { 8, new List<KeyValuePair<int, string>> {
        new(0, "Generic"),
    }},

    // 9 = Recipe
    { 9, new List<KeyValuePair<int, string>> {
        new(0, "Book"),
        new(1, "Leatherworking"),
        new(2, "Tailoring"),
        new(3, "Engineering"),
        new(4, "Blacksmithing"),
        new(5, "Cooking"),
        new(6, "Alchemy"),
        new(7, "First Aid"),
        new(8, "Enchanting"),
        new(9, "Fishing"),
        new(10, "Jewelcrafting"),
    }},

    // 10 = Money
    { 10, new List<KeyValuePair<int, string>> {
        new(0, "Money"),
    }},

    // 11 = Quiver
    { 11, new List<KeyValuePair<int, string>> {
        new(0, "Quiver"),
        new(1, "Ammo Pouch"),
    }},

    // 12 = Quest
    { 12, new List<KeyValuePair<int, string>> {
        new(0, "Quest"),
    }},

    // 13 = Key
    { 13, new List<KeyValuePair<int, string>> {
        new(0, "Key"),
        new(1, "Lockpick"),
    }},

    // 14 = Permanent
    { 14, new List<KeyValuePair<int, string>> {
        new(0, "Permanent"),
    }},

    // 15 = Miscellaneous
    { 15, new List<KeyValuePair<int, string>> {
        new(0, "Junk"),
        new(1, "Reagent"),
        new(2, "Pet"),
        new(3, "Holiday"),
        new(4, "Other"),
        new(5, "Mount"),
    }},

    // 16 = Glyph (class-specific glyphs in WotLK)
    { 16, new List<KeyValuePair<int, string>> {
        new(1, "Warrior"),
        new(2, "Paladin"),
        new(3, "Hunter"),
        new(4, "Rogue"),
        new(5, "Priest"),
        new(6, "Death Knight"),
        new(7, "Shaman"),
        new(8, "Mage"),
        new(9, "Warlock"),
        new(11, "Druid"),
    }},
};

        private void InitItemClassAndSubclasses()
        {
            if (ItemClassBox == null || ItemSubclassBox == null)
                return;

            ItemClassBox.Items.Clear();
            foreach (var kv in _itemClassNames)
            {
                ItemClassBox.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });
            }

            // Default to Consumable if nothing selected
            if (ItemClassBox.SelectedIndex < 0)
                ItemClassBox.SelectedIndex = 0;

            PopulateItemSubclassesFromClass();
        }

        private void ItemClassBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateItemSubclassesFromClass();
        }

        private void PopulateItemSubclassesFromClass()
        {
            if (ItemClassBox == null || ItemSubclassBox == null)
                return;

            int cls = ComboTagInt(ItemClassBox);

            ItemSubclassBox.Items.Clear();

            if (_itemSubclassesByClass.TryGetValue(cls, out var list))
            {
                foreach (var pair in list)
                    ItemSubclassBox.Items.Add(new ComboBoxItem { Content = pair.Value, Tag = pair.Key });

                // Default to first subclass
                if (ItemSubclassBox.Items.Count > 0)
                    ItemSubclassBox.SelectedIndex = 0;
            }
            else
            {
                // Fallback: at least provide 0
                ItemSubclassBox.Items.Add(new ComboBoxItem { Content = "0", Tag = 0 });
                ItemSubclassBox.SelectedIndex = 0;
            }
        }

        // ===================== TOGGLES =====================

        private void ItemAdvancedToggle_Changed(object sender, RoutedEventArgs e)
        {
            ItemAdvancedGrid.Visibility = (ItemAdvancedToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void QuestAdvancedToggle_Changed(object sender, RoutedEventArgs e)
        {
            QuestAdvancedGrid.Visibility = (QuestAdvancedToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }



        // ===================== QUEST EXTRA PANELS (Trinity-style toggles) =====================

        private void QuestOptionToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (QuestFlagsGroup != null)
                QuestFlagsGroup.Visibility = (QuestShowFlagsToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (QuestSpecialFlagsGroup != null)
                QuestSpecialFlagsGroup.Visibility = (QuestShowSpecialFlagsToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (QuestClassesGroup != null)
                QuestClassesGroup.Visibility = (QuestShowClassesToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (QuestRacesGroup != null)
                QuestRacesGroup.Visibility = (QuestShowRacesToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void QuestFlagCheck_Changed(object sender, RoutedEventArgs e)
        {
            QuestFlagsBox.Text = ComputeBitmaskFromChecks(QuestFlagsGroup, "QuestFlag_").ToString();
        }

        private void QuestSpecialFlagCheck_Changed(object sender, RoutedEventArgs e)
        {
            QuestSpecialFlagsBox.Text = ComputeBitmaskFromChecks(QuestSpecialFlagsGroup, "QuestSpecial_").ToString();
        }

        private void QuestClassCheck_Changed(object sender, RoutedEventArgs e)
        {
            QuestClassesBox.Text = ComputeBitmaskFromChecks(QuestClassesGroup, "QuestClass_").ToString();
        }

        private void QuestRaceCheck_Changed(object sender, RoutedEventArgs e)
        {
            QuestRacesBox.Text = ComputeBitmaskFromChecks(QuestRacesGroup, "QuestRace_").ToString();
        }

        // ===================== ITEM EXTRA PANELS (Trinity-style toggles) =====================

        private void ItemOptionToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ItemFlagsGroup != null)
                ItemFlagsGroup.Visibility = (ItemShowFlagsCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemAllowableClassGroup != null)
                ItemAllowableClassGroup.Visibility = (ItemShowClassesCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemAllowableRaceGroup != null)
                ItemAllowableRaceGroup.Visibility = (ItemShowRacesCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemResistGroup != null)
                ItemResistGroup.Visibility = (ItemShowResistsCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ItemClassMask_Changed(object sender, RoutedEventArgs e) => UpdateItemAllowableClassMask();
        private void ItemRaceMask_Changed(object sender, RoutedEventArgs e) => UpdateItemAllowableRaceMask();

        private void ItemClassesAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecks(ItemClassChecksGrid, true);
            UpdateItemAllowableClassMask();
        }

        private void ItemClassesNone_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecks(ItemClassChecksGrid, false);
            UpdateItemAllowableClassMask();
        }

        private void ItemRacesAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecks(ItemRaceChecksGrid, true);
            UpdateItemAllowableRaceMask();
        }

        private void ItemRacesNone_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecks(ItemRaceChecksGrid, false);
            UpdateItemAllowableRaceMask();
        }

        private void SetAllChecks(Panel panel, bool isChecked)
        {
            if (panel == null) return;
            foreach (var child in panel.Children)
            {
                if (child is CheckBox cb) cb.IsChecked = isChecked;
            }
        }

        private void UpdateItemAllowableClassMask()
        {
            if (ItemAllowableClassMaskBox == null || ItemClassChecksGrid == null) return;

            int mask = 0;
            foreach (var child in ItemClassChecksGrid.Children)
            {
                if (child is CheckBox cb && cb.IsChecked == true)
                {
                    int bit = 0;
                    int.TryParse(cb.Tag?.ToString() ?? "0", out bit);
                    mask |= bit;
                }
            }
            ItemAllowableClassMaskBox.Text = mask.ToString(CultureInfo.InvariantCulture);
        }

        private void UpdateItemAllowableRaceMask()
        {
            if (ItemAllowableRaceMaskBox == null || ItemRaceChecksGrid == null) return;

            int mask = 0;
            foreach (var child in ItemRaceChecksGrid.Children)
            {
                if (child is CheckBox cb && cb.IsChecked == true)
                {
                    int bit = 0;
                    int.TryParse(cb.Tag?.ToString() ?? "0", out bit);
                    mask |= bit;
                }
            }
            ItemAllowableRaceMaskBox.Text = mask.ToString(CultureInfo.InvariantCulture);
        }

        private void ItemPreview_Changed(object sender, TextChangedEventArgs e) => UpdateItemPreview();
        private void ItemPreview_Changed(object sender, SelectionChangedEventArgs e) => UpdateItemPreview();


        private void UpdateItemPreview()
        {
            if (ItemPreviewName == null || ItemPreviewMeta == null) return;

            string name = string.IsNullOrWhiteSpace(ItemNameBox?.Text) ? "(name)" : ItemNameBox.Text.Trim();
            int displayId = ParseInt(ItemDisplayIdBox?.Text ?? "0", 0);

            int quality = 0;
            string qualityText = "Unknown";
            var qItem = ItemQualityBox?.SelectedItem as ComboBoxItem;
            if (qItem != null)
            {
                int.TryParse(qItem.Tag?.ToString() ?? "0", out quality);
                qualityText = qItem.Content?.ToString() ?? "Unknown";
            }

            ItemPreviewName.Text = name;
            ItemPreviewMeta.Text = $"DisplayID: {displayId} | Quality: {quality} ({qualityText})";
        }

        // ===================== CREATURE TEMPLATE =====================

        private void CreatureLoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            string name = "Default";
            var item = CreatureTemplateCombo.SelectedItem as ComboBoxItem;
            if (item != null) name = item.Content.ToString();

            // base defaults
            CreatureMinLevelBox.Text = "1";
            CreatureMaxLevelBox.Text = "1";
            CreatureFactionCombo.SelectedIndex = 0; // friendly
            CreatureTypeCombo.SelectedIndex = 0;
            CreatureFamilyCombo.SelectedIndex = 0;
            CreatureDisplayIdBox.Text = "0";

            // clear flags
            ClearChecks(_npcFlags);
            ClearChecks(_unitFlags);
            ClearChecks(_unitFlags2);
            ClearChecks(_extraFlags);

            // speech defaults
            SpeechEnable.IsChecked = false;
            _speechLines.Clear();
            SpeechLinesList.ItemsSource = null;
            SpeechLinesList.ItemsSource = _speechLines;

            if (name.Contains("Questgiver"))
            {
                SetCheckByLabel(_npcFlags, "Gossip", true);
                SetCheckByLabel(_npcFlags, "Questgiver", true);
                CreatureFactionCombo.SelectedIndex = 0;
                SetCheckByLabel(_unitFlags, "Non-attackable", true);
            }
            else if (name.Contains("Vendor"))
            {
                SetCheckByLabel(_npcFlags, "Vendor", true);
                // vendor often has gossip too
                SetCheckByLabel(_npcFlags, "Gossip", true);
                CreatureFactionCombo.SelectedIndex = 0;
                SetCheckByLabel(_unitFlags, "Non-attackable", true);
            }
            else if (name.Contains("Herald"))
            {
                // friendly talker that uses speech lines
                SetCheckByLabel(_npcFlags, "Gossip", true);
                CreatureFactionCombo.SelectedIndex = 0;
                SetCheckByLabel(_unitFlags, "Non-attackable", true);
                SpeechEnable.IsChecked = true;
                SpeechGroupIdBox.Text = "0";
                SpeechTypeCombo.SelectedIndex = 1; // YELL feels “herald”
            }
            else if (name.Contains("Beast Enemy"))
            {
                CreatureFactionCombo.SelectedIndex = 2; // hostile
                SelectComboByTag(CreatureTypeCombo, 1); // Beast
                SelectComboByTag(CreatureFamilyCombo, 1); // Wolf
            }
            else if (name.Contains("Humanoid Enemy"))
            {
                CreatureFactionCombo.SelectedIndex = 2; // hostile
                SelectComboByTag(CreatureTypeCombo, 7); // Humanoid
            }
            else if (name.Contains("Raid Boss"))
            {
                CreatureFactionCombo.SelectedIndex = 2;
                CreatureMinLevelBox.Text = "80";
                CreatureMaxLevelBox.Text = "80";
                SetCheckByLabel(_extraFlags, "No XP", true);
            }

            ConnMessage.Text = "Loaded template: " + name;
        }


        // ===================== ROLE PRESETS =====================

        private void CreatureApplyRolePreset_Click(object sender, RoutedEventArgs e)
        {
            string preset = "Default";
            var item = CreatureRolePresetCombo?.SelectedItem as ComboBoxItem;
            if (item != null) preset = item.Content?.ToString() ?? "Default";

            ApplyRolePreset(preset);
            ConnMessage.Text = "Applied role preset: " + preset;
        }

        private void ApplyRolePreset(string preset)
        {
            // Only touch faction + flags (beginner-friendly)
            ClearChecks(_npcFlags);
            ClearChecks(_unitFlags);
            ClearChecks(_unitFlags2);
            ClearChecks(_extraFlags);

            // Speech is only auto-enabled for the clue herald preset
            if (preset != "Clue Herald Template")
            {
                // don't destroy existing speech lines; just don't force-enable
                // user can toggle Speech manually
            }

            switch (preset)
            {
                case "Friendly NPC":
                    SelectComboByTag(CreatureFactionCombo, 35);
                    SetCheckByLabel(_npcFlags, "Gossip", true);
                    SetCheckByLabel(_unitFlags, "Non-attackable", true);
                    break;

                case "Questgiver":
                    SelectComboByTag(CreatureFactionCombo, 35);
                    SetCheckByLabel(_npcFlags, "Gossip", true);
                    SetCheckByLabel(_npcFlags, "Questgiver", true);
                    SetCheckByLabel(_unitFlags, "Non-attackable", true);
                    break;

                case "Vendor":
                    SelectComboByTag(CreatureFactionCombo, 35);
                    SetCheckByLabel(_npcFlags, "Gossip", true);
                    SetCheckByLabel(_npcFlags, "Vendor", true);
                    // Optional: Repair commonly goes with vendors; user can toggle it.
                    SetCheckByLabel(_unitFlags, "Non-attackable", true);
                    break;

                case "Trainer":
                    SelectComboByTag(CreatureFactionCombo, 35);
                    SetCheckByLabel(_npcFlags, "Gossip", true);
                    SetCheckByLabel(_npcFlags, "Trainer", true);
                    SetCheckByLabel(_unitFlags, "Non-attackable", true);
                    break;

                case "Guard":
                    SelectComboByTag(CreatureFactionCombo, 35);
                    // We don't include a "Guard" npcflag checkbox in beginner list.
                    // Leave flags mostly empty so user can decide.
                    break;

                case "Hostile Enemy":
                    SelectComboByTag(CreatureFactionCombo, 14);
                    break;

                case "Clue Herald Template":
                    SelectComboByTag(CreatureFactionCombo, 35);
                    SetCheckByLabel(_npcFlags, "Gossip", true);
                    SetCheckByLabel(_unitFlags, "Non-attackable", true);
                    SpeechEnable.IsChecked = true;
                    break;

                default:
                    // Default: no changes
                    break;
            }

            UpdateQuestPreview();
        }

        private void ClearChecks(Dictionary<CheckBox, uint> map)
        {
            foreach (var kv in map) kv.Key.IsChecked = false;
        }

        private void SetCheckByLabel(Dictionary<CheckBox, uint> map, string label, bool value)
        {
            foreach (var kv in map)
            {
                if (kv.Key.Content != null && kv.Key.Content.ToString() == label)
                {
                    kv.Key.IsChecked = value;
                    return;
                }
            }
        }

        private void SelectComboByTag(ComboBox cb, int tagValue)
        {
            for (int i = 0; i < cb.Items.Count; i++)
            {
                var it = cb.Items[i] as ComboBoxItem;
                if (it != null && it.Tag != null)
                {
                    int v = 0;
                    int.TryParse(it.Tag.ToString(), out v);
                    if (v == tagValue)
                    {
                        cb.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        // ===================== SPEECH =====================

        private void SpeechAddLine_Click(object sender, RoutedEventArgs e)
        {
            if (SpeechEnable.IsChecked != true)
            {
                MessageBox.Show("Enable Speech first.", "Speech", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int groupId = ParseInt(SpeechGroupIdBox.Text, 0);
            string type = "SAY";
            var sel = SpeechTypeCombo.SelectedItem as ComboBoxItem;
            if (sel != null) type = sel.Content.ToString();

            string text = (SpeechTextBox.Text ?? "").Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("Speech text is empty.", "Speech", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int sound = ParseInt(SpeechSoundBox.Text, 0);
            int emote = ParseInt(SpeechEmoteBox.Text, 0);

            _speechLines.Add(new SpeechLine
            {
                GroupId = groupId,
                Type = type,
                Text = text,
                Sound = sound,
                Emote = emote
            });

            SpeechTextBox.Text = "";
            SpeechLinesList.ItemsSource = null;
            SpeechLinesList.ItemsSource = _speechLines;
        }

        private void SpeechRemoveLine_Click(object sender, RoutedEventArgs e)
        {
            var selected = SpeechLinesList.SelectedItem as SpeechLine;
            if (selected == null) return;
            _speechLines.Remove(selected);
            SpeechLinesList.ItemsSource = null;
            SpeechLinesList.ItemsSource = _speechLines;
        }

        // ===================== CREATURE SQL =====================

        private void CreatureGenerateSql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _lastSql = BuildCreatureSql();
                SqlPreviewBox.Text = _lastSql;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Creature SQL failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreatureApplyDb_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected)
            {
                MessageBox.Show("Not connected.", "DB", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string sql = BuildCreatureSql();
                ExecuteSql(sql);
                _lastSql = sql;
                SqlPreviewBox.Text = sql;
                MessageBox.Show("Creature applied to DB.", "DB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Apply failed: " + ex.Message, "DB Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreatureExportSql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string sql = BuildCreatureSql();
                _lastSql = sql;
                SqlPreviewBox.Text = sql;

                string file = ExportToFile(sql, "creature");
                MessageBox.Show("Exported: " + file, "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildCreatureSql()
        {
            int entry = ParseInt(CreatureEntryBox.Text, 0);
            if (entry <= 0) throw new Exception("Creature Entry must be > 0.");

            string name = SqlEscape(CreatureNameBox.Text);
            string subname = SqlEscape(CreatureSubnameBox.Text);

            int minLevel = ParseInt(CreatureMinLevelBox.Text, 1);
            int maxLevel = ParseInt(CreatureMaxLevelBox.Text, minLevel);

            int faction = ComboTagInt(CreatureFactionCombo);
            int creatureType = ComboTagInt(CreatureTypeCombo);
            int family = ComboTagInt(CreatureFamilyCombo);

            int displayId = ParseInt(CreatureDisplayIdBox.Text, 0);

            uint npcflag = SumFlags(_npcFlags);
            uint unitFlags = SumFlags(_unitFlags);
            uint unitFlags2 = SumFlags(_unitFlags2);
            uint flagsExtra = SumFlags(_extraFlags);

            // We insert with explicit columns (AzerothCore/TC style)
            // Missing columns will error, which is good (tells us schema mismatch).
            var sb = new StringBuilder();
            sb.AppendLine("-- AzerothCore Creator - Creature");
            sb.AppendLine("START TRANSACTION;");
            sb.AppendLine("SET @ENTRY := " + entry + ";");

            sb.AppendLine("DELETE FROM `creature_text` WHERE `CreatureID`=@ENTRY;");
            sb.AppendLine("DELETE FROM `creature` WHERE `id1`=@ENTRY OR `id`=@ENTRY;");
            sb.AppendLine("DELETE FROM `creature_template` WHERE `entry`=@ENTRY;");
            sb.AppendLine();

            sb.Append("INSERT INTO `creature_template` ");
            sb.Append("(`entry`,`name`,`subname`,`minlevel`,`maxlevel`,`faction`,`npcflag`,`unit_flags`,`unit_flags2`,`type`,`family`,`flags_extra`,`modelid1`) VALUES ");
            sb.AppendFormat("(@ENTRY,'{0}','{1}',{2},{3},{4},{5},{6},{7},{8},{9},{10},{11});",
                name, subname, minLevel, maxLevel, faction, npcflag, unitFlags, unitFlags2, creatureType, family, flagsExtra, displayId);
            sb.AppendLine();
            sb.AppendLine();// Spawn optional (some AC schemas have id1 instead of id; we try both via columns)
            if (CreatureSpawnEnable.IsChecked == true)
            {
                int map = ParseInt(CreatureMapBox.Text, 0);
                double x = ParseDouble(CreatureXBox.Text, 0);
                double y = ParseDouble(CreatureYBox.Text, 0);
                double z = ParseDouble(CreatureZBox.Text, 0);
                double o = ParseDouble(CreatureOBox.Text, 0);
                int respawn = ParseInt(CreatureRespawnBox.Text, 120);

                // We will insert using the classic TC columns when possible.
                // If your creature table differs, we’ll adjust next.
                sb.AppendLine("-- Spawn (creature) (AzerothCore uses creature.id1)");
                sb.Append("INSERT INTO `creature` ");
                sb.Append("(`id1`,`map`,`position_x`,`position_y`,`position_z`,`orientation`,`spawntimesecs`) VALUES ");
                sb.AppendFormat("(@ENTRY,{0},{1},{2},{3},{4},{5});",
                    map,
                    x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture),
                    z.ToString(CultureInfo.InvariantCulture),
                    o.ToString(CultureInfo.InvariantCulture),
                    respawn);
                sb.AppendLine();
                sb.AppendLine();
            }

            // Speech optional (creature_text)
            if (SpeechEnable.IsChecked == true && _speechLines.Count > 0)
            {
                sb.AppendLine("-- Speech (creature_text)");
                // Creature_text usually has: CreatureID, GroupID, ID, Text, Type, Language, Probability, Emote, Duration, Sound, BroadcastTextId, TextRange, comment
                // We will fill a minimal safe subset.
                int nextLineId = 0;
                for (int i = 0; i < _speechLines.Count; i++)
                {
                    var line = _speechLines[i];
                    int type = 12; // SAY default
                    if (line.Type == "YELL") type = 14;
                    else if (line.Type == "WHISPER") type = 15;

                    sb.Append("INSERT INTO `creature_text` ");
                    sb.Append("(`CreatureID`,`GroupID`,`ID`,`Text`,`Type`,`Language`,`Probability`,`Emote`,`Duration`,`Sound`,`comment`) VALUES ");
                    sb.AppendFormat("(@ENTRY,{0},{1},'{2}',{3},0,100,{4},0,{5},'{6}');",
                        line.GroupId,
                        nextLineId,
                        SqlEscape(line.Text),
                        type,
                        line.Emote,
                        line.Sound,
                        "AcoreCreator");
                    sb.AppendLine();
                    nextLineId++;
                }
                sb.AppendLine();
            }

            sb.AppendLine("COMMIT;");
            return sb.ToString();
        }

        // ===================== ITEM SQL =====================

        private void ItemGenerateSql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _lastSql = BuildItemSql();
                SqlPreviewBox.Text = _lastSql;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Item SQL failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ItemApplyDb_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected)
            {
                MessageBox.Show("Not connected.", "DB", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string sql = BuildItemSql();
                ExecuteSql(sql);
                _lastSql = sql;
                SqlPreviewBox.Text = sql;
                MessageBox.Show("Item applied to DB.", "DB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Apply failed: " + ex.Message, "DB Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ItemExportSql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string sql = BuildItemSql();
                _lastSql = sql;
                SqlPreviewBox.Text = sql;

                string file = ExportToFile(sql, "item");
                MessageBox.Show("Exported: " + file, "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildItemSql()
        {
            int entry = ParseInt(ItemEntryBox.Text, 0);
            if (entry <= 0) throw new Exception("Item Entry must be > 0.");

            string name = SqlEscape(ItemNameBox.Text);
            int displayId = ParseInt(ItemDisplayIdBox.Text, 0);

            int quality = ComboTagInt(ItemQualityBox);
            int cls = ComboTagInt(ItemClassBox);
            int subcls = ComboTagInt(ItemSubclassBox);
            int inv = ComboTagInt(ItemInventoryTypeBox);

            int reqLevel = ParseInt(ItemReqLevelBox.Text, 0);
            int itemLevel = ParseInt(ItemLevelBox.Text, 1);
            int stack = ParseInt(ItemStackableBox.Text, 1);

            int buyPrice = ParseInt(ItemBuyPriceBox.Text, 0);
            int sellPrice = ParseInt(ItemSellPriceBox.Text, 0);
            int bonding = ComboTagInt(ItemBondingBox);
            int armor = ParseInt(ItemArmorBox.Text, 0);
            int dmgMin = ParseInt(ItemDmgMinBox.Text, 0);
            int dmgMax = ParseInt(ItemDmgMaxBox.Text, 0);
            // Trinity-style optional panels (safe defaults even if hidden)
            int flags = ParseInt(ItemFlagsBox?.Text ?? "0", 0);
            int flagsExtra = ParseInt(ItemFlagsExtraBox?.Text ?? "0", 0);
            int allowableClass = ParseInt(ItemAllowableClassMaskBox?.Text ?? "0", 0);
            int allowableRace = ParseInt(ItemAllowableRaceMaskBox?.Text ?? "0", 0);

            int holyRes = ParseInt(ItemHolyResBox?.Text ?? "0", 0);
            int fireRes = ParseInt(ItemFireResBox?.Text ?? "0", 0);
            int natureRes = ParseInt(ItemNatureResBox?.Text ?? "0", 0);
            int frostRes = ParseInt(ItemFrostResBox?.Text ?? "0", 0);
            int shadowRes = ParseInt(ItemShadowResBox?.Text ?? "0", 0);
            int arcaneRes = ParseInt(ItemArcaneResBox?.Text ?? "0", 0);


            var sb = new StringBuilder();
            sb.AppendLine("-- AzerothCore Creator - Item");
            sb.AppendLine("START TRANSACTION;");
            sb.AppendLine("SET @ENTRY := " + entry + ";");
            sb.AppendLine("DELETE FROM `item_template` WHERE `entry`=@ENTRY;");
            sb.AppendLine();

            // We insert a starter subset. Remaining columns will take default values.
            sb.AppendLine("-- AzerothCore Creator - Item");
            sb.AppendLine("START TRANSACTION;");
            sb.AppendLine($"SET @ENTRY := {entry};");
            sb.AppendLine("DELETE FROM `item_template` WHERE `entry`=@ENTRY;");
            sb.AppendLine();

            sb.Append("INSERT INTO `item_template` ");
            sb.Append("(`entry`,`class`,`subclass`,`name`,`displayid`,`Quality`,`BuyPrice`,`SellPrice`,`InventoryType`,`RequiredLevel`,`ItemLevel`,`stackable`,`bonding`,`armor`,`dmg_min1`,`dmg_max1`,");
            sb.Append("`Flags`,`FlagsExtra`,`AllowableClass`,`AllowableRace`,`holy_res`,`fire_res`,`nature_res`,`frost_res`,`shadow_res`,`arcane_res`) VALUES ");

            sb.AppendFormat(
                "(@ENTRY,{0},{1},'{2}',{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25});",
                cls, subcls, name.Replace("'", "''"), displayId, quality, buyPrice, sellPrice, inv, reqLevel, itemLevel, stack, bonding, armor, dmgMin, dmgMax,
                flags, flagsExtra, allowableClass, allowableRace, holyRes, fireRes, natureRes, frostRes, shadowRes, arcaneRes);

            sb.AppendLine();
            sb.AppendLine("COMMIT;");
            return sb.ToString();

        }

        // ===================== QUEST SQL =====================

        private void QuestGenerateSql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _lastSql = BuildQuestSql();
                SqlPreviewBox.Text = _lastSql;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Quest SQL failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuestApplyDb_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected)
            {
                MessageBox.Show("Not connected.", "DB", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string sql = BuildQuestSql();
                ExecuteSql(sql);
                _lastSql = sql;
                SqlPreviewBox.Text = sql;
                MessageBox.Show("Quest applied to DB.", "DB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Apply failed: " + ex.Message, "DB Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuestExportSql_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string sql = BuildQuestSql();
                _lastSql = sql;
                SqlPreviewBox.Text = sql;

                string file = ExportToFile(sql, "quest");
                MessageBox.Show("Exported: " + file, "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildQuestSql()
        {
            int id = ParseInt(QuestIdBox.Text, 0);
            if (id <= 0) throw new Exception("Quest ID must be > 0.");

            string title = SqlEscape(QuestTitleBox.Text);
            string details = SqlEscape(QuestDetailsBox.Text);
            string objectives = SqlEscape(QuestObjectivesBox.Text);

            int minLevel = ParseInt(QuestMinLevelBox.Text, 1);
            int questLevel = ParseInt(QuestLevelBox.Text, 1);
            int rewardXp = ParseInt(QuestRewardXpBox.Text, 0);
            int rewardMoney = ParseInt(QuestRewardMoneyBox.Text, 0);

            int qType = ParseInt(QuestTypeBox.Text, 0);
            int qSort = ParseInt(QuestSortBox.Text, 0);

            int questFlags = ParseInt(QuestFlagsBox.Text, 0);
            int allowableRaces = ParseInt(QuestRacesBox.Text, 0);
            int specialFlags = ParseInt(QuestSpecialFlagsBox.Text, 0);
            int allowableClasses = ParseInt(QuestClassesBox.Text, 0);

            int prevQuestId = ParseInt(QuestPrevQuestIdBox.Text, 0);
            int nextQuestId = ParseInt(QuestNextQuestIdBox.Text, 0);

            // required items
            int r1 = ParseInt(ReqItemId1.Text, 0); int rc1 = ParseInt(ReqItemCount1.Text, 0);
            int r2 = ParseInt(ReqItemId2.Text, 0); int rc2 = ParseInt(ReqItemCount2.Text, 0);
            int r3 = ParseInt(ReqItemId3.Text, 0); int rc3 = ParseInt(ReqItemCount3.Text, 0);
            int r4 = ParseInt(ReqItemId4.Text, 0); int rc4 = ParseInt(ReqItemCount4.Text, 0);
            int r5 = ParseInt(ReqItemId5.Text, 0); int rc5 = ParseInt(ReqItemCount5.Text, 0);
            int r6 = ParseInt(ReqItemId6.Text, 0); int rc6 = ParseInt(ReqItemCount6.Text, 0);

            // required npc/go
            int n1 = ParseInt(ReqNpcGoId1.Text, 0); int nc1 = ParseInt(ReqNpcGoCount1.Text, 0);
            int n2 = ParseInt(ReqNpcGoId2.Text, 0); int nc2 = ParseInt(ReqNpcGoCount2.Text, 0);
            int n3 = ParseInt(ReqNpcGoId3.Text, 0); int nc3 = ParseInt(ReqNpcGoCount3.Text, 0);
            int n4 = ParseInt(ReqNpcGoId4.Text, 0); int nc4 = ParseInt(ReqNpcGoCount4.Text, 0);

            // reward items
            int w1 = ParseInt(RewardItemId1.Text, 0); int wc1 = ParseInt(RewardItemCount1.Text, 0);
            int w2 = ParseInt(RewardItemId2.Text, 0); int wc2 = ParseInt(RewardItemCount2.Text, 0);
            int w3 = ParseInt(RewardItemId3.Text, 0); int wc3 = ParseInt(RewardItemCount3.Text, 0);
            int w4 = ParseInt(RewardItemId4.Text, 0); int wc4 = ParseInt(RewardItemCount4.Text, 0);

            // choice items
            int c1 = ParseInt(ChoiceItemId1.Text, 0); int cc1 = ParseInt(ChoiceItemCount1.Text, 0);
            int c2 = ParseInt(ChoiceItemId2.Text, 0); int cc2 = ParseInt(ChoiceItemCount2.Text, 0);
            int c3 = ParseInt(ChoiceItemId3.Text, 0); int cc3 = ParseInt(ChoiceItemCount3.Text, 0);
            int c4 = ParseInt(ChoiceItemId4.Text, 0); int cc4 = ParseInt(ChoiceItemCount4.Text, 0);
            int c5 = ParseInt(ChoiceItemId5.Text, 0); int cc5 = ParseInt(ChoiceItemCount5.Text, 0);
            int c6 = ParseInt(ChoiceItemId6.Text, 0); int cc6 = ParseInt(ChoiceItemCount6.Text, 0);

            // reputation rewards
            int f1 = ParseInt(RewardFactionId1.Text, 0); int fv1 = ParseInt(RewardFactionValue1.Text, 0);
            int f2 = ParseInt(RewardFactionId2.Text, 0); int fv2 = ParseInt(RewardFactionValue2.Text, 0);
            int f3 = ParseInt(RewardFactionId3.Text, 0); int fv3 = ParseInt(RewardFactionValue3.Text, 0);
            int f4 = ParseInt(RewardFactionId4.Text, 0); int fv4 = ParseInt(RewardFactionValue4.Text, 0);
            int f5 = ParseInt(RewardFactionId5.Text, 0); int fv5 = ParseInt(RewardFactionValue5.Text, 0);

            var sb = new StringBuilder();
            sb.AppendLine("-- AzerothCore Creator - Quest");
            sb.AppendLine("START TRANSACTION;");
            sb.AppendLine("SET @ID := " + id + ";");
            sb.AppendLine("DELETE FROM `quest_template` WHERE `ID`=@ID;");
            sb.AppendLine("DELETE FROM `quest_template_addon` WHERE `ID`=@ID;");
            sb.AppendLine();

            sb.Append("INSERT INTO `quest_template` ");
            sb.Append("(`ID`,`LogTitle`,`QuestDescription`,`Objectives`,`MinLevel`,`QuestLevel`,`QuestType`,`QuestSortID`,`Flags`,`AllowableRaces`,");
            sb.Append("`RewardXPId`,`RewardMoney`,");
            sb.Append("`RequiredItemId1`,`RequiredItemId2`,`RequiredItemId3`,`RequiredItemId4`,`RequiredItemId5`,`RequiredItemId6`,");
            sb.Append("`RequiredItemCount1`,`RequiredItemCount2`,`RequiredItemCount3`,`RequiredItemCount4`,`RequiredItemCount5`,`RequiredItemCount6`,");
            sb.Append("`RequiredNpcOrGo1`,`RequiredNpcOrGo2`,`RequiredNpcOrGo3`,`RequiredNpcOrGo4`,");
            sb.Append("`RequiredNpcOrGoCount1`,`RequiredNpcOrGoCount2`,`RequiredNpcOrGoCount3`,`RequiredNpcOrGoCount4`,");
            sb.Append("`RewardItem1`,`RewardItem2`,`RewardItem3`,`RewardItem4`,");
            sb.Append("`RewardAmount1`,`RewardAmount2`,`RewardAmount3`,`RewardAmount4`,");
            sb.Append("`RewardChoiceItemID1`,`RewardChoiceItemID2`,`RewardChoiceItemID3`,`RewardChoiceItemID4`,`RewardChoiceItemID5`,`RewardChoiceItemID6`,");
            sb.Append("`RewardChoiceItemQuantity1`,`RewardChoiceItemQuantity2`,`RewardChoiceItemQuantity3`,`RewardChoiceItemQuantity4`,`RewardChoiceItemQuantity5`,`RewardChoiceItemQuantity6`,");
            sb.Append("`RewardFactionID1`,`RewardFactionID2`,`RewardFactionID3`,`RewardFactionID4`,`RewardFactionID5`,");
            sb.Append("`RewardFactionValue1`,`RewardFactionValue2`,`RewardFactionValue3`,`RewardFactionValue4`,`RewardFactionValue5`,");
            sb.Append("`RewardFactionOverride1`,`RewardFactionOverride2`,`RewardFactionOverride3`,`RewardFactionOverride4`,`RewardFactionOverride5`");
            sb.Append(") VALUES (");
            sb.AppendFormat("@ID,'{0}','{1}','{2}',{3},{4},{5},{6},{7},{8},{9},{10},",
                title, details, objectives, minLevel, questLevel, qType, qSort, questFlags, allowableRaces, rewardXp, rewardMoney);

            sb.AppendFormat("{0},{1},{2},{3},{4},{5},", r1, r2, r3, r4, r5, r6);
            sb.AppendFormat("{0},{1},{2},{3},{4},{5},", rc1, rc2, rc3, rc4, rc5, rc6);

            sb.AppendFormat("{0},{1},{2},{3},", n1, n2, n3, n4);
            sb.AppendFormat("{0},{1},{2},{3},", nc1, nc2, nc3, nc4);

            sb.AppendFormat("{0},{1},{2},{3},", w1, w2, w3, w4);
            sb.AppendFormat("{0},{1},{2},{3},", wc1, wc2, wc3, wc4);

            sb.AppendFormat("{0},{1},{2},{3},{4},{5},", c1, c2, c3, c4, c5, c6);
            sb.AppendFormat("{0},{1},{2},{3},{4},{5},", cc1, cc2, cc3, cc4, cc5, cc6);

            sb.AppendFormat("{0},{1},{2},{3},{4},", f1, f2, f3, f4, f5);
            sb.AppendFormat("{0},{1},{2},{3},{4},", fv1, fv2, fv3, fv4, fv5);

            // overrides default to 0
            sb.Append("0,0,0,0,0");

            sb.Append(");");
            sb.AppendLine();
            sb.AppendLine();

            sb.AppendLine("INSERT INTO `quest_template_addon` (`ID`,`SpecialFlags`,`AllowableClasses`,`PrevQuestID`,`NextQuestID`) VALUES (@ID,"
                          + specialFlags + "," + allowableClasses + "," + prevQuestId + "," + nextQuestId + ");");
            sb.AppendLine("COMMIT;");
            return sb.ToString();
        }


        // ===================== EXECUTE / EXPORT =====================

        private void ExecuteSql(string sql)
        {
            using (var conn = new MySqlConnection(BuildConnString()))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private string ExportToFile(string sql, string prefix)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AcoreCreatorExports");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string file = Path.Combine(dir, string.Format("{0}_{1:yyyyMMdd_HHmmss}.sql", prefix, DateTime.Now));
            File.WriteAllText(file, sql, Encoding.UTF8);
            return file;
        }

        // ===================== HELPERS =====================

        private static int ParseInt(string s, int fallback)
        {
            int v;
            if (int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static double ParseDouble(string s, double fallback)
        {
            double v;
            if (double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static string SqlEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("'", "''");
        }
        private void ItemFlagCheck_Changed(object sender, RoutedEventArgs e)
        {
            ItemFlagsBox.Text = ComputeBitmaskFromChecks(ItemFlagsGroup, "Flag_").ToString();
        }

        private void ItemExtraFlagCheck_Changed(object sender, RoutedEventArgs e)
        {
            ItemFlagsExtraBox.Text = ComputeBitmaskFromChecks(ItemFlagsGroup, "Extra_").ToString();
        }

        private static long ComputeBitmaskFromChecks(DependencyObject root, string prefix)
        {
            long value = 0;

            foreach (var cb in FindVisualChildren<CheckBox>(root))
            {
                if (cb.Name.StartsWith(prefix) && cb.IsChecked == true && cb.Tag != null)
                {
                    if (long.TryParse(cb.Tag.ToString(), out long bit))
                        value |= bit;
                }
            }

            return value;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                    yield return t;

                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }


        // ---------------------------
        // Quest Chunk C: Dynamic row helpers (Required/Reward/Choice items)
        // ---------------------------
        private void QuestAddRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;

            (string rowPrefix, int maxRows) = tag switch
            {
                "ReqItem" => ("ReqItemRow", 6),
                "RewardItem" => ("RewardItemRow", 4),
                "ChoiceItem" => ("ChoiceItemRow", 6),
                "ReqNpcGo" => ("ReqNpcGoRow", 4),
                "RepFaction" => ("RepFactionRow", 5),
                _ => ("", 0)
            };

            if (maxRows == 0) return;

            // Reveal the next hidden row (rows start at 1; row 1 is always visible)
            for (int i = 2; i <= maxRows; i++)
            {
                if (FindName(rowPrefix + i) is FrameworkElement row && row.Visibility != Visibility.Visible)
                {
                    row.Visibility = Visibility.Visible;
                    break;
                }
            }
        }


        private void QuestRemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;

            // Expected: "ReqItem:2"  "RewardItem:3"  "ChoiceItem:5"  "ReqNpcGo:2"  "RepFaction:4"
            var parts = tag.Split(':');
            if (parts.Length != 2) return;

            var group = parts[0];
            if (!int.TryParse(parts[1], out int index)) return;
            if (index <= 1) return;

            string rowName = group switch
            {
                "ReqItem" => "ReqItemRow" + index,
                "RewardItem" => "RewardItemRow" + index,
                "ChoiceItem" => "ChoiceItemRow" + index,
                "ReqNpcGo" => "ReqNpcGoRow" + index,
                "RepFaction" => "RepFactionRow" + index,
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(rowName)) return;

            if (FindName(rowName) is FrameworkElement row)
                row.Visibility = Visibility.Collapsed;

            // Clear associated textboxes so hidden rows don't contribute accidentally.
            string idBox = group switch
            {
                "ReqItem" => "ReqItemId" + index,
                "RewardItem" => "RewardItemId" + index,
                "ChoiceItem" => "ChoiceItemId" + index,
                "ReqNpcGo" => "ReqNpcGoId" + index,
                "RepFaction" => "RewardFactionId" + index,
                _ => ""
            };

            string countBox = group switch
            {
                "ReqItem" => "ReqItemCount" + index,
                "RewardItem" => "RewardItemCount" + index,
                "ChoiceItem" => "ChoiceItemCount" + index,
                "ReqNpcGo" => "ReqNpcGoCount" + index,
                "RepFaction" => "RewardFactionValue" + index,
                _ => ""
            };

            if (FindName(idBox) is TextBox idTb) idTb.Text = "0";
            if (FindName(countBox) is TextBox cntTb) cntTb.Text = "0";

            UpdateQuestPreview();
        }


        private void QuestRowValueChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not FrameworkElement fe || string.IsNullOrWhiteSpace(fe.Name))
                return;

            // If a user starts filling a hidden row (via copy/paste or programmatic set),
            // ensure that row becomes visible.
            void EnsureVisible(string rowPrefix, int idx)
            {
                if (idx <= 1) return;
                if (FindName(rowPrefix + idx) is FrameworkElement row)
                    row.Visibility = Visibility.Visible;
            }

            // Example names:
            // ReqItemId3, ReqItemCount3, RewardItemId2, ChoiceItemCount6
            // ReqNpcGoId2, ReqNpcGoCount4
            // RewardFactionId3, RewardFactionValue5
            var m = System.Text.RegularExpressions.Regex.Match(fe.Name,
                @"^(ReqItem|RewardItem|ChoiceItem|ReqNpcGo|RewardFaction)(Id|Count|Value)(\d+)$");
            if (!m.Success) { UpdateQuestPreview(); return; }

            var group = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[3].Value, out int idx)) { UpdateQuestPreview(); return; }

            switch (group)
            {
                case "ReqItem":
                    EnsureVisible("ReqItemRow", idx);
                    break;
                case "RewardItem":
                    EnsureVisible("RewardItemRow", idx);
                    break;
                case "ChoiceItem":
                    EnsureVisible("ChoiceItemRow", idx);
                    break;
                case "ReqNpcGo":
                    EnsureVisible("ReqNpcGoRow", idx);
                    break;
                case "RewardFaction":
                    EnsureVisible("RepFactionRow", idx);
                    break;
            }

            UpdateQuestPreview();
        }



        // ===================== QUEST PREVIEW (polish) =====================

        private void QuestPreview_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateQuestPreview();
        }

        private void UpdateQuestPreview()
        {
            if (QuestPreviewTitle == null) return; // XAML not loaded yet

            string title = (QuestTitleBox?.Text ?? "").Trim();
            string details = (QuestDetailsBox?.Text ?? "").Trim();
            string objText = (QuestObjectivesBox?.Text ?? "").Trim();

            QuestPreviewTitle.Text = string.IsNullOrWhiteSpace(title) ? "(Quest Title)" : title;
            QuestPreviewDetails.Text = string.IsNullOrWhiteSpace(details) ? "(Quest Details)" : details;

            // Build objectives: include the freeform objectives text + required items + required npc/go if present.
            var objSb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(objText))
            {
                objSb.AppendLine(objText);
            }

            var reqItemLines = BuildIdCountLines("ReqItemId", "ReqItemCount", 6, prefix: "Collect");
            if (reqItemLines.Count > 0)
            {
                if (objSb.Length > 0) objSb.AppendLine();
                foreach (var line in reqItemLines) objSb.AppendLine(line);
            }

            var reqNpcGoLines = BuildIdCountLines("ReqNpcGoId", "ReqNpcGoCount", 4, prefix: "Defeat/Use");
            if (reqNpcGoLines.Count > 0)
            {
                if (objSb.Length > 0) objSb.AppendLine();
                foreach (var line in reqNpcGoLines) objSb.AppendLine(line);
            }

            QuestPreviewObjectives.Text = objSb.Length == 0 ? "(Objectives)" : objSb.ToString().TrimEnd();

            // Build rewards (XP / Money + reward/choice items + reputation)
            var rewSb = new StringBuilder();

            int.TryParse(QuestRewardXpBox?.Text?.Trim(), out int xp);
            long.TryParse(QuestRewardMoneyBox?.Text?.Trim(), out long money);

            if (xp > 0) rewSb.AppendLine("XP: " + xp);
            if (money > 0) rewSb.AppendLine("Money: " + money + " copper");

            var rewardItems = BuildIdCountLines("RewardItemId", "RewardItemCount", 4, prefix: "Item");
            if (rewardItems.Count > 0)
            {
                if (rewSb.Length > 0) rewSb.AppendLine();
                rewSb.AppendLine("Reward items:");
                foreach (var line in rewardItems) rewSb.AppendLine("• " + line);
            }

            var choiceItems = BuildIdCountLines("ChoiceItemId", "ChoiceItemCount", 6, prefix: "Choice");
            if (choiceItems.Count > 0)
            {
                if (rewSb.Length > 0) rewSb.AppendLine();
                rewSb.AppendLine("Choice rewards:");
                foreach (var line in choiceItems) rewSb.AppendLine("• " + line);
            }

            var repLines = BuildIdCountLines("RewardFactionId", "RewardFactionValue", 5, prefix: "Faction");
            if (repLines.Count > 0)
            {
                if (rewSb.Length > 0) rewSb.AppendLine();
                rewSb.AppendLine("Reputation:");
                foreach (var line in repLines) rewSb.AppendLine("• " + line);
            }

            QuestPreviewRewards.Text = rewSb.Length == 0 ? "(Rewards)" : rewSb.ToString().TrimEnd();
        }


        private List<string> BuildIdCountLines(string idPrefix, string countPrefix, int max, string prefix)
        {
            var lines = new List<string>();

            for (int i = 1; i <= max; i++)
            {
                if (!(FindName(idPrefix + i) is TextBox idTb) || !(FindName(countPrefix + i) is TextBox cntTb))
                    continue;

                int.TryParse(idTb.Text.Trim(), out int id);
                int.TryParse(cntTb.Text.Trim(), out int cnt);

                if (id <= 0 || cnt <= 0) continue;

                lines.Add($"{prefix} {id} x{cnt}");
            }

            return lines;
        }

        private void DiscordLink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.gg/nVFp9ec",
                UseShellExecute = true
            });
        }
    }

}
