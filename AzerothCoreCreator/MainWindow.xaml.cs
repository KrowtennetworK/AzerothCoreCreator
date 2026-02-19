using MySqlConnector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;


namespace AzerothCoreCreator
{
    public partial class MainWindow : Window
    {
        private bool _connected = false;
        private string _lastSql = "";

        // Rotating status tips (bottom-left)
        private readonly string[] _statusTips = new[]
        {
            "Tip: Install via Setup.exe to enable automatic updates.",
            "Tip: Use Export .sql to share changes with your team.",
            "Tip: New releases every few days.",
            "Tip: Check out our private server!  Join the Discord!",
            "Tip: Use Find buttons to browse IDs quickly."
        };
        private int _statusTipIndex = 0;
        private DispatcherTimer? _statusTipTimer;



        // Cached faction (Faction.dbc / faction_dbc) lookup list for fast searching
        private List<LookupRow> _factionCache = null;
        private string _factionCacheTable = null;
        private string _factionCacheIdCol = null;
        private string _factionCacheNameCol = null;
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
            HookItemPreviewEvents();
            UpdateItemPreview();
            LockQuestSectionsToSingleRow();



            // Item tab extras (safe defaults)
            ItemOptionToggle_Changed(null, null);
            UpdateItemAllowableClassMask();
            UpdateItemAllowableRaceMask();
            UpdateItemPreview();
            StartStatusTipRotator();
        }

        private void StartStatusTipRotator()
        {
            // Avoid double-starting if Window_Loaded fires again for any reason
            if (_statusTipTimer != null)
                return;

            // Set an initial tip immediately
            if (StatusTipText != null && _statusTips.Length > 0)
                StatusTipText.Text = _statusTips[0];

            _statusTipTimer = new DispatcherTimer();
            _statusTipTimer.Interval = TimeSpan.FromSeconds(20);
            _statusTipTimer.Tick += (s, e) =>
            {
                if (StatusTipText == null || _statusTips.Length == 0)
                    return;

                _statusTipIndex++;
                if (_statusTipIndex >= _statusTips.Length)
                    _statusTipIndex = 0;

                StatusTipText.Text = _statusTips[_statusTipIndex];
            };
            _statusTipTimer.Start();            

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

                    // Pre-load faction name list for quest lookups (Faction.dbc)
                    EnsureFactionCache(conn);
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
            // Quality
            ItemQualityBox.Items.Clear();
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Poor (0)", Tag = 0 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Common (1)", Tag = 1 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Uncommon (2)", Tag = 2 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Rare (3)", Tag = 3 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Epic (4)", Tag = 4 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Legendary (5)", Tag = 5 });
            ItemQualityBox.Items.Add(new ComboBoxItem { Content = "Artifact (6)", Tag = 6 });
            ItemQualityBox.SelectedIndex = 1;

            // Item Class/Subclass (dynamic like Trinity Creator)
            InitItemClassAndSubclasses();

            // Inventory Type (Equip Slot) options depend on Class/Subclass
            PopulateItemInventoryTypesFromClassSubclass();

            // Hook dropdown change events (do this in code-behind so XAML stays simple)
            ItemClassBox.SelectionChanged -= ItemClassBox_SelectionChanged;
            ItemSubclassBox.SelectionChanged -= ItemSubclassBox_SelectionChanged;

            ItemClassBox.SelectionChanged += ItemClassBox_SelectionChanged;
            ItemSubclassBox.SelectionChanged += ItemSubclassBox_SelectionChanged;

            // Bonding
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
            QuestSortBox.Text = "0";

            // Quest Type dropdown (QuestInfoID)
            if (QuestTypeCombo != null && QuestTypeCombo.Items.Count == 0)
            {
                void AddType(int id, string name)
                {
                    QuestTypeCombo.Items.Add(new ComboBoxItem { Content = $"{id} - {name}", Tag = id });
                }

                AddType(0, "Normal");
                AddType(1, "Group");
                AddType(21, "Life");
                AddType(41, "PvP");
                AddType(62, "Raid");
                AddType(81, "Dungeon");
                AddType(82, "World Event");
                AddType(83, "Legendary");
                AddType(84, "Escort");
                AddType(85, "Heroic");
                AddType(88, "Raid (10/25)");

                QuestTypeCombo.SelectedIndex = 0;
            }
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

        // Returns ComboBoxItem.Tag as int (used when Tag stores DB values).
        // Falls back to defaultValue if nothing is selected or Tag isn't parseable.
        private int GetSelectedComboTagInt(ComboBox cb, int defaultValue)
        {
            if (cb?.SelectedItem is not ComboBoxItem item) return defaultValue;
            if (item.Tag == null) return defaultValue;
            return int.TryParse(item.Tag.ToString(), out int v) ? v : defaultValue;
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
            UpdateContainerUiVisibility();
        }

        private void ItemClassBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateItemSubclassesFromClass();
            UpdateContainerUiVisibility();

            // Convenience: when switching to Container, automatically enable Advanced Fields
            // so bag/container options are visible without an extra click.
            if (ItemAdvancedToggle != null && ItemClassBox != null)
            {
                int cls = ComboTagInt(ItemClassBox);
                if (cls == 1)
                    ItemAdvancedToggle.IsChecked = true;
            }
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


                PopulateItemInventoryTypesFromClassSubclass();
            }
            else
            {
                // Fallback: at least provide 0
                ItemSubclassBox.Items.Add(new ComboBoxItem { Content = "0", Tag = 0 });
                ItemSubclassBox.SelectedIndex = 0;


                PopulateItemInventoryTypesFromClassSubclass();
            }
        }


        private void ItemSubclassBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateItemInventoryTypesFromClassSubclass();
            UpdateContainerUiVisibility();
        }

        private void UpdateContainerUiVisibility()
        {
            // Trinity behavior: only show container/bag options when Class = Container (1)
            if (ItemContainerGroup == null || ItemClassBox == null)
                return;

            int cls = ComboTagInt(ItemClassBox);
            ItemContainerGroup.Visibility = (cls == 1) ? Visibility.Visible : Visibility.Collapsed;

            if (cls != 1)
            {
                // If we leave container mode, clear values back to safe defaults.
                if (ItemContainerSlotsBox != null)
                    ItemContainerSlotsBox.Text = "0";

                SetBagFamilyChecks(false);
            }
        }

        private void SetBagFamilyChecks(bool isChecked)
        {
            void Set(CheckBox? cb) { if (cb != null) cb.IsChecked = isChecked; }

            Set(BagFamilyArrows);
            Set(BagFamilyBullets);
            Set(BagFamilySoulShards);
            Set(BagFamilyLeatherworking);
            Set(BagFamilyInscription);
            Set(BagFamilyHerbs);
            Set(BagFamilyEnchanting);
            Set(BagFamilyEngineering);
            Set(BagFamilyKeys);
            Set(BagFamilyGems);
            Set(BagFamilyMining);
            Set(BagFamilySoulbound);
            Set(BagFamilyVanityPets);
            Set(BagFamilyCurrency);
            Set(BagFamilyQuestItems);
        }

        [Flags]
        private enum BagFamilyMask
        {
            None = 0,
            Arrows = 0x00000001,
            Bullets = 0x00000002,
            SoulShards = 0x00000004,
            Leatherworking = 0x00000008,
            Inscription = 0x00000010,
            Herbs = 0x00000020,
            Enchanting = 0x00000040,
            Engineering = 0x00000080,
            Keys = 0x00000100,
            Gems = 0x00000200,
            Mining = 0x00000400,
            SoulboundEquipment = 0x00000800,
            VanityPets = 0x00001000,
            CurrencyTokens = 0x00002000,
            QuestItems = 0x00004000,
        }

        private int BuildBagFamilyMask()
        {
            BagFamilyMask mask = BagFamilyMask.None;

            if (BagFamilyArrows?.IsChecked == true) mask |= BagFamilyMask.Arrows;
            if (BagFamilyBullets?.IsChecked == true) mask |= BagFamilyMask.Bullets;
            if (BagFamilySoulShards?.IsChecked == true) mask |= BagFamilyMask.SoulShards;
            if (BagFamilyLeatherworking?.IsChecked == true) mask |= BagFamilyMask.Leatherworking;
            if (BagFamilyInscription?.IsChecked == true) mask |= BagFamilyMask.Inscription;
            if (BagFamilyHerbs?.IsChecked == true) mask |= BagFamilyMask.Herbs;
            if (BagFamilyEnchanting?.IsChecked == true) mask |= BagFamilyMask.Enchanting;
            if (BagFamilyEngineering?.IsChecked == true) mask |= BagFamilyMask.Engineering;
            if (BagFamilyKeys?.IsChecked == true) mask |= BagFamilyMask.Keys;
            if (BagFamilyGems?.IsChecked == true) mask |= BagFamilyMask.Gems;
            if (BagFamilyMining?.IsChecked == true) mask |= BagFamilyMask.Mining;
            if (BagFamilySoulbound?.IsChecked == true) mask |= BagFamilyMask.SoulboundEquipment;
            if (BagFamilyVanityPets?.IsChecked == true) mask |= BagFamilyMask.VanityPets;
            if (BagFamilyCurrency?.IsChecked == true) mask |= BagFamilyMask.CurrencyTokens;
            if (BagFamilyQuestItems?.IsChecked == true) mask |= BagFamilyMask.QuestItems;

            return (int)mask;
        }

        // Inventory Types (INVTYPE_*) we care about for WotLK
        private static readonly Dictionary<int, string> _inventoryTypeNames = new()
{
    { 0,  "Non-equipable (0)" },
    { 1,  "Head (1)" },
    { 2,  "Neck (2)" },
    { 3,  "Shoulder (3)" },
    { 4,  "Shirt (4)" },
    { 5,  "Chest (5)" },
    { 6,  "Waist (6)" },
    { 7,  "Legs (7)" },
    { 8,  "Feet (8)" },
    { 9,  "Wrist (9)" },
    { 10, "Hands (10)" },
    { 11, "Finger (11)" },
    { 12, "Trinket (12)" },
    { 13, "One-Hand (13)" },              // INVTYPE_WEAPON
    { 14, "Shield (14)" },
    { 15, "Ranged (15)" },                // bows/guns/crossbows
    { 16, "Back (16)" },                  // cloak
    { 17, "Two-Hand (17)" },
    { 18, "Bag (18)" },
    { 19, "Tabard (19)" },
    { 20, "Robe (20)" },
    { 21, "Main Hand (21)" },             // INVTYPE_WEAPONMAINHAND
    { 22, "Off Hand (22)" },              // INVTYPE_WEAPONOFFHAND
    { 23, "Holdable (23)" },              // INVTYPE_HOLDABLE
    { 24, "Ammo (24)" },
    { 25, "Thrown (25)" },
    { 26, "Ranged (Right) (26)" },        // wands
    { 27, "Quiver (27)" },
    { 28, "Relic (28)" },
};

        private void PopulateItemInventoryTypesFromClassSubclass()
        {
            if (ItemInventoryTypeBox == null || ItemClassBox == null || ItemSubclassBox == null)
                return;

            int cls = ComboTagInt(ItemClassBox);
            int sub = ComboTagInt(ItemSubclassBox);

            ItemInventoryTypeBox.Items.Clear();

            void AddInv(int id)
            {
                if (_inventoryTypeNames.TryGetValue(id, out var label))
                    ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
                else
                    ItemInventoryTypeBox.Items.Add(new ComboBoxItem { Content = id.ToString(), Tag = id });
            }

            // Defaults: non-equipable unless class/subclass implies otherwise
            bool any = false;

            if (cls == 4) // Armor
            {
                // Armor subclasses (WotLK): 0 misc, 1 cloth, 2 leather, 3 mail, 4 plate, 5 buckler, 6 shield, 7-10 relics
                if (sub == 5 || sub == 6)
                {
                    AddInv(14); // Shield
                    any = true;
                }
                else if (sub >= 7 && sub <= 10)
                {
                    AddInv(28); // Relic
                    any = true;
                }
                else if (sub == 0)
                {
                    // Armor Misc in Trinity is mostly jewelry + cloak
                    AddInv(2);  // Neck
                    AddInv(11); // Finger
                    AddInv(12); // Trinket
                    AddInv(16); // Back (cloak)
                    any = true;
                }
                else
                {
                    // Wearable armor pieces
                    AddInv(1);  // Head
                    AddInv(3);  // Shoulder
                    AddInv(5);  // Chest
                    AddInv(6);  // Waist
                    AddInv(7);  // Legs
                    AddInv(8);  // Feet
                    AddInv(9);  // Wrist
                    AddInv(10); // Hands
                                // (Cloaks are handled by sub==0; shirt/tabard are separate classes in DB)
                    any = true;
                }
            }
            else if (cls == 2) // Weapon
            {
                // Weapon subclasses (WotLK): see ItemSubclassesByClass mapping above
                // 0 1h Axe, 1 2h Axe, 2 Bow, 3 Gun, 4 1h Mace, 5 2h Mace, 6 Polearm, 7 1h Sword, 8 2h Sword, 10 Staff,
                // 13 Fist, 14 Misc, 15 Dagger, 16 Thrown, 18 Crossbow, 19 Wand, 20 Fishing Pole
                switch (sub)
                {
                    case 2:  // Bow
                    case 3:  // Gun
                    case 18: // Crossbow
                        AddInv(15); // Ranged
                        any = true;
                        break;

                    case 19: // Wand
                        AddInv(26); // Ranged (Right)
                        any = true;
                        break;

                    case 16: // Thrown
                        AddInv(25); // Thrown
                        any = true;
                        break;

                    case 1:  // 2h Axe
                    case 5:  // 2h Mace
                    case 6:  // Polearm
                    case 8:  // 2h Sword
                    case 10: // Staff
                    case 20: // Fishing Pole
                        AddInv(17); // Two-Hand
                        any = true;
                        break;

                    case 14: // Misc
                             // Trinity shows a broader list here
                        AddInv(13); // One-Hand
                        AddInv(17); // Two-Hand
                        AddInv(21); // Main Hand
                        AddInv(22); // Off Hand
                        AddInv(23); // Holdable
                        AddInv(15); // Ranged
                        AddInv(25); // Thrown
                        AddInv(26); // Ranged (Right)
                        any = true;
                        break;

                    default:
                        // Most 1H weapons & daggers/fist can be weapon/mainhand/offhand
                        AddInv(13); // One-Hand
                        AddInv(21); // Main Hand
                        AddInv(22); // Off Hand
                        any = true;
                        break;
                }
            }
            else if (cls == 1) // Container
            {
                AddInv(18); // Bag
                any = true;
            }
            else if (cls == 11) // Quiver
            {
                AddInv(27); // Quiver
                any = true;
            }
            else
            {
                // Most other classes aren't equippable in inventory slot terms
                AddInv(0);
                any = true;
            }

            if (!any)
            {
                AddInv(0);
            }

            if (ItemInventoryTypeBox.Items.Count > 0)
                ItemInventoryTypeBox.SelectedIndex = 0;
        }

        // ===================== TOGGLES =====================

        private void QuestAdvancedToggle_Changed(object sender, RoutedEventArgs e)
        {
            QuestAdvancedGrid.Visibility =
                QuestAdvancedToggle.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (QuestAdvancedToggle.IsChecked == true)
                LockQuestSectionsToSingleRow();
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
            // Main "Show Advanced Fields" toggle (basic advanced subset)
            if (ItemAdvancedGrid != null)
                ItemAdvancedGrid.Visibility = (ItemAdvancedToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemFlagsGroup != null)
                ItemFlagsGroup.Visibility = (ItemShowFlagsCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemAllowableClassGroup != null)
                ItemAllowableClassGroup.Visibility = (ItemShowClassesCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemAllowableRaceGroup != null)
                ItemAllowableRaceGroup.Visibility = (ItemShowRacesCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (ItemResistGroup != null)
                ItemResistGroup.Visibility = (ItemShowResistsCheck.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            // Container group lives inside ItemAdvancedGrid, but we still need to decide if it's relevant.
            UpdateContainerUiVisibility();
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
        private void ItemPreview_Changed(object sender, RoutedEventArgs e)
        {
            UpdateItemPreview();
        }



        private sealed class ItemStatRow
        {
            public string Name { get; set; } = "";
            public int Value { get; set; }
        }

        private readonly List<ItemStatRow> _itemStats = new();

        private void ItemStatAdd_Click(object sender, RoutedEventArgs e)
        {
            if (ItemStatTypeBox?.SelectedItem is not ComboBoxItem statItem)
                return;

            string statName = (statItem.Content?.ToString() ?? "").Trim();
            int val = ParseInt(ItemStatValueBox?.Text ?? "0", 0);

            if (string.IsNullOrWhiteSpace(statName) || val == 0)
                return;

            var existing = _itemStats.FirstOrDefault(s => s.Name.Equals(statName, StringComparison.Ordinal));
            if (existing != null)
                existing.Value += val;
            else
                _itemStats.Add(new ItemStatRow { Name = statName, Value = val });

            RefreshItemStatsUI();
            UpdateItemPreview();
        }

        private void ItemStatRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ItemStatRow row)
            {
                _itemStats.Remove(row);
                RefreshItemStatsUI();
                UpdateItemPreview();
            }
        }

        private void RefreshItemStatsUI()
        {
            if (ItemStatsList == null) return;
            ItemStatsList.ItemsSource = null;
            ItemStatsList.ItemsSource = _itemStats.ToList();
        }

        private void UpdateItemPreview()
        {
            // TrinityCreator-style tooltip preview controls (must exist in XAML)
            var nameTb = FindFirstByName<TextBlock>("ItemTooltipName");
            var bindTb = FindFirstByName<TextBlock>("ItemTooltipBind");
            var slotTb = FindFirstByName<TextBlock>("ItemTooltipSlot");
            var rightTb = FindFirstByName<TextBlock>("ItemTooltipRight");
            var dmgTb = FindFirstByName<TextBlock>("ItemTooltipDamage");
            var speedTb = FindFirstByName<TextBlock>("ItemTooltipSpeed");
            var dpsTb = FindFirstByName<TextBlock>("ItemTooltipDps");
            var armorTb = FindFirstByName<TextBlock>("ItemTooltipArmor");
            var reqTb = FindFirstByName<TextBlock>("ItemTooltipReqLevel");
            var ilvlTb = FindFirstByName<TextBlock>("ItemTooltipItemLevel");
            var statsCtl = FindFirstByName<ItemsControl>("ItemTooltipStats");
            var flavorTb = FindFirstByName<TextBlock>("ItemTooltipFlavor");
            var classesTb = FindFirstByName<TextBlock>("ItemTooltipClasses");
            var durTb = FindFirstByName<TextBlock>("ItemTooltipDurability");

            if (nameTb == null) return; // preview not present

            // Item inputs
            var nameBox = FindFirstByName<TextBox>(
                "ItemNameBox", "ItemNameTextBox", "ItemName", "ItemNameTxt", "ItemNameField");

            var qualityBox = FindFirstByName<ComboBox>(
                "ItemQualityBox", "ItemQuality", "ItemQualityCombo", "ItemQualityComboBox");

            var classBox = FindFirstByName<ComboBox>(
                "ItemClassBox", "ItemClass", "ItemClassCombo", "ItemClassComboBox");

            var subclassBox = FindFirstByName<ComboBox>(
                "ItemSubclassBox", "ItemSubclass", "ItemSubclassCombo", "ItemSubclassComboBox");

            var invBox = FindFirstByName<ComboBox>(
                "ItemInventoryTypeBox", "ItemInventoryType", "ItemEquipSlotBox", "ItemEquipSlot");

            var bondingBox = FindFirstByName<ComboBox>(
                "ItemBondingBox", "ItemBonding", "ItemBondingCombo", "ItemBondingComboBox");

            var armorBox = FindFirstByName<TextBox>("ItemArmorBox", "ItemArmor");
            var dmgMinBox = FindFirstByName<TextBox>("ItemDmgMinBox", "ItemDamageMinBox");
            var dmgMaxBox = FindFirstByName<TextBox>("ItemDmgMaxBox", "ItemDamageMaxBox");
            var speedBox = FindFirstByName<TextBox>("ItemSpeedBox", "ItemSpeedMsBox", "ItemSpeed", "ItemSpeedMs", "ItemWeaponSpeedBox");
            var reqLevelBox = FindFirstByName<TextBox>("ItemReqLevelBox", "ItemRequiredLevelBox");
            var itemLevelBox = FindFirstByName<TextBox>("ItemLevelBox", "ItemItemLevelBox");
            var stackBox = FindFirstByName<TextBox>("ItemStackableBox", "ItemStackCountBox");

            string name = (nameBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "(name)";

            int quality = 0;
            string qualityText = "";
            if (qualityBox?.SelectedItem is ComboBoxItem qItem)
            {
                int.TryParse(qItem.Tag?.ToString() ?? "0", out quality);
                qualityText = qItem.Content?.ToString() ?? "";
            }

            string clsText = "";
            if (classBox?.SelectedItem is ComboBoxItem cItem)
                clsText = cItem.Content?.ToString() ?? "";

            string subText = "";
            if (subclassBox?.SelectedItem is ComboBoxItem sItem)
                subText = sItem.Content?.ToString() ?? "";

            string invText = "";
            if (invBox?.SelectedItem is ComboBoxItem iItem)
                invText = iItem.Content?.ToString() ?? "";

            string bindText = "";
            if (bondingBox?.SelectedItem is ComboBoxItem bItem)
                bindText = bItem.Content?.ToString() ?? "";

            int armor = ParseInt(armorBox?.Text ?? "0", 0);
            int dmgMin = ParseInt(dmgMinBox?.Text ?? "0", 0);
            int dmgMax = ParseInt(dmgMaxBox?.Text ?? "0", 0);
            int reqLevel = ParseInt(reqLevelBox?.Text ?? "0", 0);
            int itemLevel = ParseInt(itemLevelBox?.Text ?? "0", 0);
            int stackCount = ParseInt(stackBox?.Text ?? "0", 0);
            int maxDurability = ParseInt(FindFirstByName<TextBox>("ItemDurabilityBox")?.Text ?? "0", 0);
            int allowableClassMask = ParseInt(FindFirstByName<TextBox>("ItemAllowableClassMaskBox")?.Text ?? "0", 0);
            double speedRaw = ParseDoubleSafe(speedBox?.Text ?? "0");
            double speedSeconds = 0;
            if (speedRaw > 0)
                speedSeconds = (speedRaw > 50) ? (speedRaw / 1000.0) : speedRaw;

            // NAME (quality-colored)
            nameTb.Text = name;
            nameTb.Foreground = GetItemQualityBrush(quality);

            // BINDING line
            if (bindTb != null)
            {
                bindTb.Text = bindText;
                bindTb.Visibility = string.IsNullOrWhiteSpace(bindText) ? Visibility.Collapsed : Visibility.Visible;
            }

            // SLOT + RIGHT TEXT
            if (slotTb != null)
            {
                slotTb.Text = invText;
                slotTb.Visibility = string.IsNullOrWhiteSpace(invText) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (rightTb != null)
            {
                // Prefer subclass (e.g., "1h Sword") and fall back to class.
                string right = !string.IsNullOrWhiteSpace(subText) ? subText : clsText;
                rightTb.Text = right;
                rightTb.Visibility = string.IsNullOrWhiteSpace(right) ? Visibility.Collapsed : Visibility.Visible;
            }
            string dmgTypeText = "";

            if (ItemDamageTypeBox?.SelectedItem is ComboBoxItem dtItem)
                dmgTypeText = dtItem.Content?.ToString() ?? "";

            // DAMAGE + SPEED + DPS
            if (dmgTb != null)
            {
                if (dmgMin > 0 || dmgMax > 0)
                {
                    if (dmgMax <= 0) dmgMax = dmgMin;

                    string typeSuffix = "";
                    if (!string.IsNullOrWhiteSpace(dmgTypeText))
                        typeSuffix = $" {dmgTypeText.Trim()}";

                    dmgTb.Text = $"{dmgMin} - {dmgMax}{typeSuffix} Damage";
                    dmgTb.Visibility = Visibility.Visible;

                    if (speedTb != null)
                    {
                        if (speedSeconds > 0)
                        {
                            speedTb.Text = $"Speed {speedSeconds:0.00}";
                            speedTb.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            speedTb.Text = "";
                            speedTb.Visibility = Visibility.Collapsed;
                        }
                    }

                    if (dpsTb != null)
                    {
                        if (speedSeconds > 0)
                        {
                            double avg = (dmgMin + dmgMax) / 2.0;
                            double dps = avg / speedSeconds;
                            dpsTb.Text = $"({dps:0.0} damage per second)";
                            dpsTb.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            dpsTb.Text = "";
                            dpsTb.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                else
                {
                    dmgTb.Text = "";
                    dmgTb.Visibility = Visibility.Collapsed;

                    if (speedTb != null)
                    {
                        speedTb.Text = "";
                        speedTb.Visibility = Visibility.Collapsed;
                    }

                    if (dpsTb != null)
                    {
                        dpsTb.Text = "";
                        dpsTb.Visibility = Visibility.Collapsed;
                    }
                }
            }

            // ARMOR
            if (armorTb != null)
            {
                if (armor > 0)
                {
                    armorTb.Text = $"{armor} Armor";
                    armorTb.Visibility = Visibility.Visible;
                }
                else
                {
                    armorTb.Text = "";
                    armorTb.Visibility = Visibility.Collapsed;
                }
            }

            // REQ LEVEL + ITEM LEVEL
            if (reqTb != null)
            {
                if (reqLevel > 0)
                {
                    reqTb.Text = $"Requires Level {reqLevel}";
                    reqTb.Visibility = Visibility.Visible;
                }
                else
                {
                    reqTb.Text = "";
                    reqTb.Visibility = Visibility.Collapsed;
                }
            }

            if (ilvlTb != null)
            {
                if (itemLevel > 0)
                {
                    ilvlTb.Text = $"Item Level {itemLevel}";
                    ilvlTb.Visibility = Visibility.Visible;
                }
                else
                {
                    ilvlTb.Text = "";
                    ilvlTb.Visibility = Visibility.Collapsed;
                }
            }

            // CLASSES (allowed) — only show when restricted
            if (classesTb != null)
            {
                string classesLine = BuildAllowedClassesLine(allowableClassMask);
                if (!string.IsNullOrWhiteSpace(classesLine))
                {
                    classesTb.Text = classesLine;
                    classesTb.Visibility = Visibility.Visible;
                }
                else
                {
                    classesTb.Text = "";
                    classesTb.Visibility = Visibility.Collapsed;
                }
            }

            // DURABILITY (template preview shows X / X)
            if (durTb != null)
            {
                if (maxDurability > 0)
                {
                    durTb.Text = $"Durability {maxDurability} / {maxDurability}";
                    durTb.Visibility = Visibility.Visible;
                }
                else
                {
                    durTb.Text = "";
                    durTb.Visibility = Visibility.Collapsed;
                }
            }

            // STATS (from Stats UI) + MAGIC RESISTANCES
            if (statsCtl != null)
            {
                var statLines = _itemStats
                    .Where(s => s.Value != 0 && !string.IsNullOrWhiteSpace(s.Name))
                    .Select(s => $"+{s.Value} {s.Name}")
                    .ToList();

                // ---- MAGIC RESISTANCES ----
                // Pull from common AC/TC item_template resistance field names.
                // This will work even if your XAML uses slightly different control names.
                int resHoly = ParseInt(FindFirstByName<TextBox>(
                    "ItemHolyResBox", "ItemResHolyBox", "ItemHolyResistBox", "ItemHolyResistanceBox", "ItemHolyResistance")?.Text ?? "0", 0);

                int resFire = ParseInt(FindFirstByName<TextBox>(
                    "ItemFireResBox", "ItemResFireBox", "ItemFireResistBox", "ItemFireResistanceBox", "ItemFireResistance")?.Text ?? "0", 0);

                int resNature = ParseInt(FindFirstByName<TextBox>(
                    "ItemNatureResBox", "ItemResNatureBox", "ItemNatureResistBox", "ItemNatureResistanceBox", "ItemNatureResistance")?.Text ?? "0", 0);

                int resFrost = ParseInt(FindFirstByName<TextBox>(
                    "ItemFrostResBox", "ItemResFrostBox", "ItemFrostResistBox", "ItemFrostResistanceBox", "ItemFrostResistance")?.Text ?? "0", 0);

                int resShadow = ParseInt(FindFirstByName<TextBox>(
                    "ItemShadowResBox", "ItemResShadowBox", "ItemShadowResistBox", "ItemShadowResistanceBox", "ItemShadowResistance")?.Text ?? "0", 0);

                int resArcane = ParseInt(FindFirstByName<TextBox>(
                    "ItemArcaneResBox", "ItemResArcaneBox", "ItemArcaneResistBox", "ItemArcaneResistanceBox", "ItemArcaneResistance")?.Text ?? "0", 0);

                void AddRes(int value, string school)
                {
                    if (value <= 0) return; // WoW tooltips show positive resists; keep it clean
                    statLines.Add($"+{value} {school} Resistance");
                }

                AddRes(resHoly, "Holy");
                AddRes(resFire, "Fire");
                AddRes(resNature, "Nature");
                AddRes(resFrost, "Frost");
                AddRes(resShadow, "Shadow");
                AddRes(resArcane, "Arcane");
                // ---------------------------

                statsCtl.ItemsSource = statLines;
                statsCtl.Visibility = statLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }


            // FLAVOR (optional)
            if (flavorTb != null)
            {
                flavorTb.Text = "";
                flavorTb.Visibility = Visibility.Collapsed;
            }
        }

        private static double ParseDoubleSafe(string s)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;

            if (double.TryParse(s, out v))
                return v;

            return 0;
        }

        private Brush GetItemQualityBrush(int quality)
        {
            // WoW-ish quality colors
            switch (quality)
            {
                case 0: return new SolidColorBrush(Color.FromRgb(0x9D, 0x9D, 0x9D)); // Poor (gray)
                case 1: return Brushes.White;                                         // Common
                case 2: return new SolidColorBrush(Color.FromRgb(0x1E, 0xFF, 0x00)); // Uncommon (green)
                case 3: return new SolidColorBrush(Color.FromRgb(0x00, 0x70, 0xDD)); // Rare (blue)
                case 4: return new SolidColorBrush(Color.FromRgb(0xA3, 0x35, 0xEE)); // Epic (purple)
                case 5: return new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x00)); // Legendary (orange)
                case 6: return new SolidColorBrush(Color.FromRgb(0xE6, 0xCC, 0x80)); // Artifact-ish (pale gold)
                case 7: return new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0xFF)); // Heirloom-ish (cyan)
                default: return Brushes.White;
            }
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
                // Always replace whatever is currently in the preview.
                SqlPreviewBox.Clear();
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
                // Always replace whatever is currently in the preview.
                SqlPreviewBox.Clear();
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

            // Container-specific (bags). Safe defaults for non-container items.
            int containerSlots = 0;
            int bagFamily = 0;
            if (cls == 1)
            {
                containerSlots = ParseInt(ItemContainerSlotsBox?.Text ?? "0", 0);
                bagFamily = BuildBagFamilyMask();
            }

            int buyPrice = MoneyToCopper(ItemBuyGoldBox?.Text, ItemBuySilverBox?.Text, ItemBuyCopperBox?.Text);
            int sellPrice = MoneyToCopper(ItemSellGoldBox?.Text, ItemSellSilverBox?.Text, ItemSellCopperBox?.Text);
            int buyCount = Math.Max(1, ParseInt(ItemBuyCountBox?.Text ?? "1", 1));
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
            sb.Append("(`entry`,`class`,`subclass`,`name`,`displayid`,`Quality`,`BuyPrice`,`SellPrice`,`BuyCount`,`InventoryType`,`RequiredLevel`,`ItemLevel`,`stackable`,`bonding`,");
            sb.Append("`ContainerSlots`,`BagFamily`,");
            sb.Append("`armor`,`dmg_min1`,`dmg_max1`,");
            sb.Append("`Flags`,`FlagsExtra`,`AllowableClass`,`AllowableRace`,`holy_res`,`fire_res`,`nature_res`,`frost_res`,`shadow_res`,`arcane_res`) VALUES ");

            sb.AppendFormat(
                "(@ENTRY,{0},{1},'{2}',{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27});",
                cls, subcls, name.Replace("'", "''"), displayId, quality, buyPrice, sellPrice, buyCount, inv, reqLevel, itemLevel, stack, bonding,
                containerSlots, bagFamily,
                armor, dmgMin, dmgMax,
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
                // Always replace whatever is currently in the preview.
                SqlPreviewBox.Clear();
                SqlPreviewBox.Text = _lastSql;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Quest SQL failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SqlPreviewClear_Click(object sender, RoutedEventArgs e)
        {
            SqlPreviewBox.Clear();
            _lastSql = string.Empty;
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

            int qType = GetSelectedComboTagInt(QuestTypeCombo, 0);
            int qSort = ParseInt(QuestSortBox.Text, 0);

            int questFlags = ParseInt(QuestFlagsBox.Text, 0);
            int allowableRaces = ParseInt(QuestRacesBox.Text, 0);
            int specialFlags = ParseInt(QuestSpecialFlagsBox.Text, 0);
            int allowableClasses = ParseInt(QuestClassesBox.Text, 0);

            int prevQuestId = ParseInt(QuestPrevQuestIdBox.Text, 0);
            int nextQuestId = ParseInt(QuestNextQuestIdBox.Text, 0);

            int reqMinRepFaction = ParseInt(QuestReqMinRepFactionBox.Text, 0);
            int reqMinRepValue = ParseInt(QuestReqMinRepValueBox.Text, 0);
            int reqMaxRepFaction = ParseInt(QuestReqMaxRepFactionBox.Text, 0);
            int reqMaxRepValue = ParseInt(QuestReqMaxRepValueBox.Text, 0);


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
            sb.Append("(`ID`,`LogTitle`,`QuestDescription`,`Objectives`,`MinLevel`,`QuestLevel`,`QuestInfoID`,`QuestSortID`,`Flags`,`AllowableRaces`,");
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

            sb.AppendLine("INSERT INTO `quest_template_addon` (`ID`,`SpecialFlags`,`AllowableClasses`,`PrevQuestID`,`NextQuestID`,`RequiredMinRepFaction`,`RequiredMinRepValue`,`RequiredMaxRepFaction`,`RequiredMaxRepValue`) VALUES (@ID,"
                          + specialFlags + "," + allowableClasses + "," + prevQuestId + "," + nextQuestId + ","
                          + reqMinRepFaction + "," + reqMinRepValue + "," + reqMaxRepFaction + "," + reqMaxRepValue + ");");
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


        private static string BuildAllowedClassesLine(int mask)
        {
            // In AzerothCore/TrinityCore: -1 or 0 commonly means "all classes" (no restriction)
            if (mask == -1 || mask == 0)
                return null;

            var names = new List<string>(12);

            // Standard class masks (WotLK era)
            if ((mask & 1) != 0) names.Add("Warrior");
            if ((mask & 2) != 0) names.Add("Paladin");
            if ((mask & 4) != 0) names.Add("Hunter");
            if ((mask & 8) != 0) names.Add("Rogue");
            if ((mask & 16) != 0) names.Add("Priest");
            if ((mask & 64) != 0) names.Add("Shaman");
            if ((mask & 128) != 0) names.Add("Mage");
            if ((mask & 256) != 0) names.Add("Warlock");
            if ((mask & 1024) != 0) names.Add("Druid");
            if ((mask & 2048) != 0) names.Add("Death Knight");

            if (names.Count == 0)
                return null;

            return "Classes: " + string.Join(", ", names);
        }

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


        // ===================== MONEY (Gold/Silver/Copper) =====================

        private bool _updatingVendorMoneyUi;

        private void MoneyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Digits only
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private static int SafeInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return int.TryParse(text.Trim(), out var v) ? v : 0;
        }

        private static int MoneyToCopper(string? goldText, string? silverText, string? copperText)
        {
            int gold = SafeInt(goldText);
            int silver = SafeInt(silverText);
            int copper = SafeInt(copperText);

            if (gold < 0) gold = 0;
            if (silver < 0) silver = 0;
            if (copper < 0) copper = 0;

            // Normalize overflow
            gold += silver / 100;
            silver %= 100;

            silver += copper / 100;
            copper %= 100;

            checked
            {
                return (gold * 10000) + (silver * 100) + copper;
            }
        }

        private static void CopperToMoney(int totalCopper, out int gold, out int silver, out int copper)
        {
            if (totalCopper < 0) totalCopper = 0;
            gold = totalCopper / 10000;
            totalCopper %= 10000;
            silver = totalCopper / 100;
            copper = totalCopper % 100;
        }

        private void SetSellMoneyFromCopper(int sellCopper)
        {
            CopperToMoney(sellCopper, out var g, out var s, out var c);
            ItemSellGoldBox.Text = g.ToString();
            ItemSellSilverBox.Text = s.ToString();
            ItemSellCopperBox.Text = c.ToString();
        }

        private void BuyMoney_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingVendorMoneyUi) return;
            if (ItemAutoSell25Check?.IsChecked != true) return;

            _updatingVendorMoneyUi = true;
            try
            {
                int buy = MoneyToCopper(ItemBuyGoldBox?.Text, ItemBuySilverBox?.Text, ItemBuyCopperBox?.Text);
                int sell = (int)Math.Floor(buy * 0.25);
                SetSellMoneyFromCopper(sell);
            }
            finally
            {
                _updatingVendorMoneyUi = false;
            }
        }

        private void SellMoney_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingVendorMoneyUi) return;

            // If user manually edits Sell while auto is enabled, disable auto.
            if (ItemAutoSell25Check?.IsChecked == true)
                ItemAutoSell25Check.IsChecked = false;
        }

        private void ItemAutoSell25Check_Changed(object sender, RoutedEventArgs e)
        {
            if (ItemAutoSell25Check?.IsChecked == true)
            {
                // Apply immediately.
                BuyMoney_TextChanged(this, null!);
            }
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


        private void QuestTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
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




        // ===================== QUEST SORT FINDER =====================
        private static readonly List<LookupEntry> _questSortEntries = new List<LookupEntry>
{
    new LookupEntry { Id = 1, Name = "Epic" },
    new LookupEntry { Id = 21, Name = "Exiled Enclave" },
    new LookupEntry { Id = 22, Name = "Seasonal" },
    new LookupEntry { Id = 23, Name = "REFUSE - old undercity one" },
    new LookupEntry { Id = 24, Name = "Herbalism" },
    new LookupEntry { Id = 25, Name = "Battlegrounds" },
    new LookupEntry { Id = 41, Name = "Day of the Dead" },
    new LookupEntry { Id = 61, Name = "Warlock" },
    new LookupEntry { Id = 81, Name = "Warrior" },
    new LookupEntry { Id = 82, Name = "Shaman" },
    new LookupEntry { Id = 101, Name = "Fishing" },
    new LookupEntry { Id = 121, Name = "Blacksmithing" },
    new LookupEntry { Id = 141, Name = "Paladin" },
    new LookupEntry { Id = 161, Name = "Mage" },
    new LookupEntry { Id = 162, Name = "Rogue" },
    new LookupEntry { Id = 181, Name = "Alchemy" },
    new LookupEntry { Id = 182, Name = "Leatherworking" },
    new LookupEntry { Id = 201, Name = "Engineering" },
    new LookupEntry { Id = 221, Name = "Treasure Map" },
    new LookupEntry { Id = 241, Name = "Tournament" },
    new LookupEntry { Id = 261, Name = "Hunter" },
    new LookupEntry { Id = 262, Name = "Priest" },
    new LookupEntry { Id = 263, Name = "Druid" },
    new LookupEntry { Id = 264, Name = "Tailoring" },
    new LookupEntry { Id = 284, Name = "Special" },
    new LookupEntry { Id = 304, Name = "Cooking" },
    new LookupEntry { Id = 324, Name = "First Aid" },
    new LookupEntry { Id = 344, Name = "Legendary" },
    new LookupEntry { Id = 364, Name = "Darkmoon Faire" },
    new LookupEntry { Id = 365, Name = "Ahn'Qiraj War" },
    new LookupEntry { Id = 366, Name = "Lunar Festival" },
    new LookupEntry { Id = 367, Name = "Reputation" },
    new LookupEntry { Id = 368, Name = "Invasion" },
    new LookupEntry { Id = 369, Name = "Midsummer" },
    new LookupEntry { Id = 370, Name = "Brewfest" },
    new LookupEntry { Id = 371, Name = "Inscription" },
    new LookupEntry { Id = 372, Name = "Death Knight" },
    new LookupEntry { Id = 373, Name = "Jewelcrafting" },
    new LookupEntry { Id = 374, Name = "Noblegarden" },
    new LookupEntry { Id = 375, Name = "Pilgrim's Bounty" },
    new LookupEntry { Id = 376, Name = "Love is in the Air" },
    new LookupEntry { Id = 377, Name = "Scourge Invasion" },
    new LookupEntry { Id = 378, Name = "Exiled Enclave" },
    new LookupEntry { Id = 379, Name = "Stormwind City" },
};

        private void QuestSortFind_Click(object sender, RoutedEventArgs e)
        {
            OpenSimpleListLookupWindow(
                title: "Find Quest Sort (QuestSortID)",
                entries: _questSortEntries,
                onPick: picked =>
                {
                    QuestSortBox.Text = picked.Id.ToString(CultureInfo.InvariantCulture);
                    UpdateQuestPreview();
                });

        }

        private void QuestFindPrev_Click(object sender, RoutedEventArgs e)
        {
            OpenLookupWindow(LookupKind.Quest, QuestPrevQuestIdBox);
        }

        private void QuestFindNext_Click(object sender, RoutedEventArgs e)
        {
            OpenLookupWindow(LookupKind.Quest, QuestNextQuestIdBox);
        }

        private void OpenSimpleListLookupWindow(string title, List<LookupEntry> entries, Action<LookupEntry> onPick)
        {
            var w = new Window
            {
                Title = title,
                Owner = this,
                Width = 720,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var searchBox = new TextBox { MinWidth = 360, Margin = new Thickness(0, 0, 8, 0) };
            var hint = new TextBlock
            {
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Text = "Search by name or ID"
            };

            var top = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(hint, Dock.Right);
            top.Children.Add(hint);
            top.Children.Add(searchBox);

            var list = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false
            };
            list.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new System.Windows.Data.Binding("Id"), Width = 100 });
            list.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Use Selected", Width = 110, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90 };
            bottom.Children.Add(ok);
            bottom.Children.Add(cancel);

            Grid.SetRow(top, 0);
            Grid.SetRow(list, 1);
            Grid.SetRow(bottom, 2);
            root.Children.Add(top);
            root.Children.Add(list);
            root.Children.Add(bottom);
            w.Content = root;

            List<LookupEntry> Filter(string q)
            {
                q = (q ?? "").Trim();
                if (q.Length == 0) return entries;

                if (int.TryParse(q, out int id))
                    return entries.Where(e => e.Id == id || e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                return entries.Where(e => e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            void Refresh()
            {
                list.ItemsSource = Filter(searchBox.Text);
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }

            searchBox.TextChanged += (s, e) => Refresh();

            void Pick()
            {
                if (list.SelectedItem is LookupEntry picked)
                {
                    onPick?.Invoke(picked);
                    w.Close();
                }
            }

            ok.Click += (s, e) => Pick();
            cancel.Click += (s, e) => w.Close();

            list.MouseDoubleClick += (s, e) => Pick();
            list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Pick(); e.Handled = true; }
                if (e.Key == Key.Escape) { w.Close(); e.Handled = true; }
            };

            w.Loaded += (s, e) =>
            {
                Refresh();
                searchBox.Focus();
                searchBox.SelectAll();
            };

            w.ShowDialog();
        }
        // ===================== QUEST LOOKUP (Finder helpers) =====================
        private TextBox _questLookupTarget;

        private void QuestLookupTarget_GotFocus(object sender, RoutedEventArgs e)
        {
            _questLookupTarget = sender as TextBox;
        }

        private void QuestFindItem_Click(object sender, RoutedEventArgs e)
        {
            OpenLookupWindow(LookupKind.Item, _questLookupTarget);
        }

        private void QuestFindNpc_Click(object sender, RoutedEventArgs e)
        {
            OpenLookupWindow(LookupKind.Creature, _questLookupTarget);
        }

        private void QuestFindGo_Click(object sender, RoutedEventArgs e)
        {
            OpenLookupWindow(LookupKind.GameObject, _questLookupTarget);
        }

        private void QuestFindFaction_Click(object sender, RoutedEventArgs e)
        {
            // Faction lookup for quest fields (e.g. ReqMinRepFaction, ReqMaxRepFaction)
            OpenLookupWindow(LookupKind.FactionTemplate, _questLookupTarget);
        }

        private enum LookupKind
        {
            Item,
            Creature,
            GameObject,
            FactionTemplate,
            Quest
        }

        private sealed class LookupRow
        {
            public long Id { get; set; }
            public string Name { get; set; }
        }

        private void OpenLookupWindow(LookupKind kind, TextBox target)
        {
            if (!_connected)
            {
                MessageBox.Show("Connect to the database first.", "Not connected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (target == null)
            {
                MessageBox.Show("Click inside an ID box first, then use a Find button.", "Select a target", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var w = new Window
            {
                Title = kind == LookupKind.Item ? "Find Item" :
                        kind == LookupKind.Creature ? "Find NPC" :
                        kind == LookupKind.GameObject ? "Find GameObject" :
                        kind == LookupKind.Quest ? "Find Quest" :
                        "Find Faction (Template)",
                Owner = this,
                Width = 720,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new DockPanel { LastChildFill = true };
            var searchBox = new TextBox { MinWidth = 360, Margin = new Thickness(0, 0, 8, 0) };
            var hint = new TextBlock
            {
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Text = "Type at least 2 characters (or an ID)"
            };
            DockPanel.SetDock(hint, Dock.Right);
            top.Children.Add(hint);
            top.Children.Add(searchBox);

            var list = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false
            };
            list.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new System.Windows.Data.Binding("Id"), Width = 100 });
            list.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Use Selected", Width = 110, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90 };
            bottom.Children.Add(ok);
            bottom.Children.Add(cancel);

            Grid.SetRow(top, 0);
            Grid.SetRow(list, 1);
            Grid.SetRow(bottom, 2);
            root.Children.Add(top);
            root.Children.Add(list);
            root.Children.Add(bottom);
            w.Content = root;

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    list.ItemsSource = RunLookup(kind, searchBox.Text.Trim());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Lookup failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            searchBox.TextChanged += (s, e) =>
            {
                timer.Stop();
                timer.Start();
            };

            void acceptSelection()
            {
                if (list.SelectedItem is LookupRow row)
                {
                    target.Text = row.Id.ToString(CultureInfo.InvariantCulture);
                    target.Focus();
                    target.CaretIndex = target.Text.Length;
                    UpdateQuestPreview();
                    w.Close();
                }
            }

            ok.Click += (s, e) => acceptSelection();
            cancel.Click += (s, e) => w.Close();
            list.MouseDoubleClick += (s, e) => acceptSelection();

            // Prime with current value if numeric
            searchBox.Text = target.Text;
            searchBox.SelectAll();
            w.ShowDialog();
        }
        private void EnsureFactionCache(MySqlConnection openConn)
        {
            if (_factionCache != null && _factionCache.Count > 0)
                return;

            // 1) Optional local override: <app>\Factions.txt (ID<TAB>Name) or (ID,Name)
            var fromFile = LoadFactionsFromFile();
            if (fromFile.Count > 0)
            {
                // convert LookupEntry -> LookupRow
                _factionCache = fromFile
                    .Select(e => new LookupRow { Id = e.Id, Name = e.Name })
                    .ToList();
                _factionCacheTable = "FILE:Factions.txt";
                _factionCacheIdCol = "ID";
                _factionCacheNameCol = "Name";
                return;
            }

            // 2) Built-in starter list (covers common factions; users can extend via Factions.txt)
            var builtIn = GetBuiltInFactions();
            // convert LookupEntry -> LookupRow
            _factionCache = builtIn
                .Select(e => new LookupRow { Id = e.Id, Name = e.Name })
                .ToList();

            // 3) If the DB has a usable source with names, merge it in (best-effort)
            try
            {
                if (openConn != null)
                {
                    var src = GetBestFactionLookupSource(openConn);
                    if (!string.IsNullOrEmpty(src.table) && !string.IsNullOrEmpty(src.idCol) && !string.IsNullOrEmpty(src.nameCol))
                    {
                        var list = new List<LookupEntry>();
                        using (var cmd = new MySqlCommand($"SELECT `{src.idCol}`, `{src.nameCol}` FROM `{src.table}` ORDER BY `{src.idCol}`", openConn))
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var id = r.IsDBNull(0) ? 0 : Convert.ToInt64(r.GetValue(0));
                                var name = r.IsDBNull(1) ? "" : r.GetString(1);
                                if (id > 0 && !string.IsNullOrWhiteSpace(name))
                                    list.Add(new LookupEntry { Id = (int)id, Name = name });
                            }
                        }

                        if (list.Count > 0)
                        {
                            // Merge by Id (DB wins if it provides a name for an existing Id)
                            var map = _factionCache.ToDictionary(x => x.Id, x => x.Name);
                            foreach (var e in list)
                                map[e.Id] = e.Name;

                            // produce LookupRow entries (not LookupEntry)
                            _factionCache = map
                                .OrderBy(k => k.Key)
                                .Select(k => new LookupRow { Id = k.Key, Name = k.Value })
                                .ToList();

                            _factionCacheTable = src.table;
                            _factionCacheIdCol = src.idCol;
                            _factionCacheNameCol = src.nameCol;
                        }
                    }
                }
            }
            catch
            {
                // swallow; we already have a usable built-in list
            }
        }

        private List<LookupEntry> LoadFactionsFromFile()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = System.IO.Path.Combine(baseDir, "Factions.txt");
                if (!System.IO.File.Exists(path))
                    return new List<LookupEntry>();

                var lines = System.IO.File.ReadAllLines(path);
                var list = new List<LookupEntry>();

                foreach (var raw in lines)
                {
                    var line = (raw ?? "").Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#")) continue;

                    // Accept: "123<TAB>Name" or "123,Name"
                    string[] parts = line.Contains("\t") ? line.Split('\t') : line.Split(',');
                    if (parts.Length < 2) continue;

                    if (!int.TryParse(parts[0].Trim(), out int id)) continue;
                    var name = string.Join(" ", parts.Skip(1)).Trim();
                    if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;

                    list.Add(new LookupEntry { Id = id, Name = name });
                }

                return list
                    .GroupBy(x => x.Id)
                    .Select(g => g.Last())
                    .OrderBy(x => x.Id)
                    .ToList();
            }
            catch
            {
                return new List<LookupEntry>();
            }
        }

        private List<LookupEntry> GetBuiltInFactions()
        {
            // This is a starter set so "Find Faction" always works even when faction_dbc is empty.
            // If you want the FULL 3.3.5 list, drop a "Factions.txt" next to the EXE with lines like:
            // 69<TAB>Darnassus
            // 72<TAB>Stormwind
            // (ID,Name also works)

            return new List<LookupEntry>
    {
        new LookupEntry{ Id=1, Name="PLAYER, Human"},
        new LookupEntry{ Id=2, Name="PLAYER, Orc"},
        new LookupEntry{ Id=3, Name="PLAYER, Dwarf"},
        new LookupEntry{ Id=4, Name="PLAYER, Night Elf"},
        new LookupEntry{ Id=5, Name="PLAYER, Undead"},
        new LookupEntry{ Id=6, Name="PLAYER, Tauren"},
        new LookupEntry{ Id=7, Name="Creature"},
        new LookupEntry{ Id=8, Name="PLAYER, Gnome"},
        new LookupEntry{ Id=9, Name="PLAYER, Troll"},
        new LookupEntry{ Id=14, Name="Monster"},
        new LookupEntry{ Id=15, Name="Defias Brotherhood"},
        new LookupEntry{ Id=16, Name="Gnoll - Riverpaw"},
        new LookupEntry{ Id=17, Name="Gnoll - Redridge"},
        new LookupEntry{ Id=18, Name="Gnoll - Shadowhide"},
        new LookupEntry{ Id=19, Name="Murloc"},
        new LookupEntry{ Id=20, Name="Undead, Scourge"},
        new LookupEntry{ Id=21, Name="Booty Bay"},
        new LookupEntry{ Id=22, Name="Beast - Spider"},
        new LookupEntry{ Id=23, Name="Beast - Boar"},
        new LookupEntry{ Id=24, Name="Worgen"},
        new LookupEntry{ Id=25, Name="Kobold"},
        new LookupEntry{ Id=26, Name="Troll, Bloodscalp"},
        new LookupEntry{ Id=27, Name="Troll, Skullsplitter"},
        new LookupEntry{ Id=28, Name="Prey"},
        new LookupEntry{ Id=29, Name="Beast - Wolf"},
        new LookupEntry{ Id=30, Name="Defias Brotherhood Traitor"},
        new LookupEntry{ Id=31, Name="Friendly"},
        new LookupEntry{ Id=32, Name="Trogg"},
        new LookupEntry{ Id=33, Name="Troll, Frostmane"},
        new LookupEntry{ Id=34, Name="Orc, Blackrock"},
        new LookupEntry{ Id=35, Name="Villain"},
        new LookupEntry{ Id=36, Name="Victim"},
        new LookupEntry{ Id=37, Name="Beast - Bear"},
        new LookupEntry{ Id=38, Name="Ogre"},
        new LookupEntry{ Id=39, Name="Kurzen's Mercenaries"},
        new LookupEntry{ Id=40, Name="Escortee"},
        new LookupEntry{ Id=41, Name="Venture Company"},
        new LookupEntry{ Id=42, Name="Beast - Raptor"},
        new LookupEntry{ Id=43, Name="Basilisk"},
        new LookupEntry{ Id=44, Name="Dragonflight, Green"},
        new LookupEntry{ Id=45, Name="Lost Ones"},
        new LookupEntry{ Id=46, Name="Blacksmithing - Armorsmithing"},
        new LookupEntry{ Id=47, Name="Ironforge"},
        new LookupEntry{ Id=48, Name="Dark Iron Dwarves"},
        new LookupEntry{ Id=49, Name="Human, Night Watch"},
        new LookupEntry{ Id=50, Name="Dragonflight, Red"},
        new LookupEntry{ Id=51, Name="Gnoll - Mosshide"},
        new LookupEntry{ Id=52, Name="Orc, Dragonmaw"},
        new LookupEntry{ Id=53, Name="Gnome - Leper"},
        new LookupEntry{ Id=54, Name="Gnomeregan Exiles"},
        new LookupEntry{ Id=55, Name="Leopard"},
        new LookupEntry{ Id=56, Name="Scarlet Crusade"},
        new LookupEntry{ Id=57, Name="Gnoll - Rothide"},
        new LookupEntry{ Id=58, Name="Beast - Gorilla"},
        new LookupEntry{ Id=59, Name="Thorium Brotherhood"},
        new LookupEntry{ Id=60, Name="Naga"},
        new LookupEntry{ Id=61, Name="Dalaran"},
        new LookupEntry{ Id=62, Name="Forlorn Spirit"},
        new LookupEntry{ Id=63, Name="Darkhowl"},
        new LookupEntry{ Id=64, Name="Grell"},
        new LookupEntry{ Id=65, Name="Furbolg"},
        new LookupEntry{ Id=66, Name="Horde Generic"},
        new LookupEntry{ Id=67, Name="Horde"},
        new LookupEntry{ Id=68, Name="Undercity"},
        new LookupEntry{ Id=69, Name="Darnassus"},
        new LookupEntry{ Id=70, Name="Syndicate"},
        new LookupEntry{ Id=71, Name="Hillsbrad Militia"},
        new LookupEntry{ Id=72, Name="Stormwind"},
        new LookupEntry{ Id=73, Name="Demon"},
        new LookupEntry{ Id=74, Name="Elemental"},
        new LookupEntry{ Id=75, Name="Spirit"},
        new LookupEntry{ Id=76, Name="Orgrimmar"},
        new LookupEntry{ Id=77, Name="Orgrimmar"},
        new LookupEntry{ Id=78, Name="Gnoll - Mudsnout"},
        new LookupEntry{ Id=79, Name="Hillsbrad, Southshore Mayor"},
        new LookupEntry{ Id=80, Name="Dragonflight, Black"},
        new LookupEntry{ Id=81, Name="Thunder Bluff"},
        new LookupEntry{ Id=82, Name="Troll, Witherbark"},
        new LookupEntry{ Id=83, Name="Leatherworking - Elemental"},
        new LookupEntry{ Id=84, Name="Quilboar, Razormane"},
        new LookupEntry{ Id=85, Name="Quilboar, Bristleback"},
        new LookupEntry{ Id=86, Name="Leatherworking - Dragonscale"},
        new LookupEntry{ Id=87, Name="Bloodsail Buccaneers"},
        new LookupEntry{ Id=88, Name="Blackfathom"},
        new LookupEntry{ Id=89, Name="Makrura"},
        new LookupEntry{ Id=90, Name="Centaur, Kolkar"},
        new LookupEntry{ Id=91, Name="Centaur, Galak"},
        new LookupEntry{ Id=92, Name="Gelkis Clan Centaur"},
        new LookupEntry{ Id=93, Name="Magram Clan Centaur"},
        new LookupEntry{ Id=94, Name="Maraudine"},
        new LookupEntry{ Id=108, Name="Theramore"},
        new LookupEntry{ Id=109, Name="Quilboar, Razorfen"},
        new LookupEntry{ Id=110, Name="Quilboar, Razormane 2"},
        new LookupEntry{ Id=111, Name="Quilboar, Deathshead"},
        new LookupEntry{ Id=128, Name="Enemy"},
        new LookupEntry{ Id=148, Name="Ambient"},
        new LookupEntry{ Id=168, Name="Nethergarde Caravan"},
        new LookupEntry{ Id=169, Name="Steamwheedle Cartel"},
        new LookupEntry{ Id=189, Name="Alliance Generic"},
        new LookupEntry{ Id=209, Name="Nethergarde"},
        new LookupEntry{ Id=229, Name="Wailing Caverns"},
        new LookupEntry{ Id=249, Name="Silithid"},
        new LookupEntry{ Id=269, Name="Silvermoon Remnant"},
        new LookupEntry{ Id=270, Name="Zandalar Tribe"},
        new LookupEntry{ Id=289, Name="Blacksmithing - Weaponsmithing"},
        new LookupEntry{ Id=309, Name="Scorpid"},
        new LookupEntry{ Id=310, Name="Beast - Bat"},
        new LookupEntry{ Id=311, Name="Titan"},
        new LookupEntry{ Id=329, Name="Taskmaster Fizzule"},
        new LookupEntry{ Id=349, Name="Ravenholdt"},
        new LookupEntry{ Id=369, Name="Gadgetzan"},
        new LookupEntry{ Id=389, Name="Gnomeregan Bug"},
        new LookupEntry{ Id=409, Name="Harpy"},
        new LookupEntry{ Id=429, Name="Burning Blade"},
        new LookupEntry{ Id=449, Name="Shadowsilk Poacher"},
        new LookupEntry{ Id=450, Name="Searing Spider"},
        new LookupEntry{ Id=469, Name="Alliance"},
        new LookupEntry{ Id=470, Name="Ratchet"},
        new LookupEntry{ Id=471, Name="Wildhammer Clan"},
        new LookupEntry{ Id=489, Name="Goblin, Dark Iron Bar Patron"},
        new LookupEntry{ Id=509, Name="The League of Arathor"},
        new LookupEntry{ Id=510, Name="The Defilers"},
        new LookupEntry{ Id=511, Name="Giant"},
        new LookupEntry{ Id=529, Name="Argent Dawn"},
        new LookupEntry{ Id=530, Name="Darkspear Trolls"},
        new LookupEntry{ Id=531, Name="Dragonflight, Bronze"},
        new LookupEntry{ Id=532, Name="Dragonflight, Blue"},
        new LookupEntry{ Id=549, Name="Leatherworking - Tribal"},
        new LookupEntry{ Id=550, Name="Engineering - Goblin"},
        new LookupEntry{ Id=551, Name="Engineering - Gnome"},
        new LookupEntry{ Id=569, Name="Blacksmithing - Hammersmithing"},
        new LookupEntry{ Id=570, Name="Blacksmithing - Axesmithing"},
        new LookupEntry{ Id=571, Name="Blacksmithing - Swordsmithing"},
        new LookupEntry{ Id=572, Name="Troll, Vilebranch"},
        new LookupEntry{ Id=573, Name="Southsea Freebooters"},
        new LookupEntry{ Id=574, Name="Caer Darrow"},
        new LookupEntry{ Id=575, Name="Furbolg, Uncorrupted"},
        new LookupEntry{ Id=576, Name="Timbermaw Hold"},
        new LookupEntry{ Id=577, Name="Everlook"},
        new LookupEntry{ Id=589, Name="Wintersaber Trainers"},
        new LookupEntry{ Id=609, Name="Cenarion Circle"},
        new LookupEntry{ Id=629, Name="Shatterspear Trolls"},
        new LookupEntry{ Id=630, Name="Ravasaur Trainers"},
        new LookupEntry{ Id=649, Name="Majordomo Executus"},
        new LookupEntry{ Id=669, Name="Beast - Carrion Bird"},
        new LookupEntry{ Id=670, Name="Beast - Cat"},
        new LookupEntry{ Id=671, Name="Beast - Crab"},
        new LookupEntry{ Id=672, Name="Beast - Crocolisk"},
        new LookupEntry{ Id=673, Name="Beast - Hyena"},
        new LookupEntry{ Id=674, Name="Beast - Owl"},
        new LookupEntry{ Id=675, Name="Beast - Scorpid"},
        new LookupEntry{ Id=676, Name="Beast - Tallstrider"},
        new LookupEntry{ Id=677, Name="Beast - Turtle"},
        new LookupEntry{ Id=678, Name="Beast - Wind Serpent"},
        new LookupEntry{ Id=679, Name="Training Dummy"},
        new LookupEntry{ Id=689, Name="Dragonflight, Black - Bait"},
        new LookupEntry{ Id=709, Name="Battleground Neutral"},
        new LookupEntry{ Id=729, Name="Frostwolf Clan"},
        new LookupEntry{ Id=730, Name="Stormpike Guard"},
        new LookupEntry{ Id=749, Name="Hydraxian Waterlords"},
        new LookupEntry{ Id=750, Name="Sulfuron Firelords"},
        new LookupEntry{ Id=769, Name="Gizlock's Dummy"},
        new LookupEntry{ Id=770, Name="Gizlock's Charm"},
        new LookupEntry{ Id=771, Name="Gizlock"},
        new LookupEntry{ Id=789, Name="Moro'gai"},
        new LookupEntry{ Id=790, Name="Spirit Guide - Alliance"},
        new LookupEntry{ Id=809, Name="Shen'dralar"},
        new LookupEntry{ Id=829, Name="Ogre (Captain Kromcrush)"},
        new LookupEntry{ Id=849, Name="Spirit Guide - Horde"},
        new LookupEntry{ Id=869, Name="Jaedenar"},
        new LookupEntry{ Id=889, Name="Warsong Outriders"},
        new LookupEntry{ Id=890, Name="Silverwing Sentinels"},
        new LookupEntry{ Id=891, Name="Alliance Forces"},
        new LookupEntry{ Id=892, Name="Horde Forces"},
        new LookupEntry{ Id=893, Name="Revantusk Trolls"},
        new LookupEntry{ Id=909, Name="Darkmoon Faire"},
        new LookupEntry{ Id=910, Name="Brood of Nozdormu"},
        new LookupEntry{ Id=911, Name="Silvermoon City"},
        new LookupEntry{ Id=912, Name="Might of Kalimdor"},
        new LookupEntry{ Id=914, Name="PLAYER, Blood Elf"},
        new LookupEntry{ Id=916, Name="Silithid Attackers"},
        new LookupEntry{ Id=917, Name="The Ironforge Brigade"},
        new LookupEntry{ Id=919, Name="RC Enemies"},
        new LookupEntry{ Id=920, Name="RC Objects"},
        new LookupEntry{ Id=921, Name="Red"},
        new LookupEntry{ Id=922, Name="Blue"},
        new LookupEntry{ Id=923, Name="Tranquillien"},
        new LookupEntry{ Id=924, Name="Farstriders"},
        new LookupEntry{ Id=925, Name="DEPRECATED"},
        new LookupEntry{ Id=926, Name="Sunstriders"},
        new LookupEntry{ Id=927, Name="Magister's Guild"},
        new LookupEntry{ Id=928, Name="PLAYER, Draenei"},
        new LookupEntry{ Id=929, Name="Scourge Invaders"},
        new LookupEntry{ Id=930, Name="Bloodmaul Clan"},
        new LookupEntry{ Id=931, Name="Exodar"},
        new LookupEntry{ Id=932, Name="Test Faction (not a real faction)"},
        new LookupEntry{ Id=933, Name="The Aldor"},
        new LookupEntry{ Id=934, Name="The Consortium"},
        new LookupEntry{ Id=935, Name="The Scryers"},
        new LookupEntry{ Id=936, Name="The Sha'tar"},
        new LookupEntry{ Id=937, Name="Shattrath City"},
        new LookupEntry{ Id=942, Name="Cenarion Expedition"},
        new LookupEntry{ Id=946, Name="Honor Hold"},
        new LookupEntry{ Id=947, Name="Thrallmar"},
        new LookupEntry{ Id=967, Name="The Violet Eye"},
        new LookupEntry{ Id=970, Name="Sporeggar"},
        new LookupEntry{ Id=978, Name="Kurenai"},
        new LookupEntry{ Id=980, Name="The Burning Crusade"},
        new LookupEntry{ Id=1011, Name="Lower City"},
        new LookupEntry{ Id=1031, Name="Sha'tari Skyguard"},
        new LookupEntry{ Id=1037, Name="Alliance Vanguard"},
        new LookupEntry{ Id=1041, Name="Frenzy"},
        new LookupEntry{ Id=1052, Name="Valgarde Combatant"},
        new LookupEntry{ Id=1073, Name="The Kalu'ak"},
        new LookupEntry{ Id=1077, Name="Shattered Sun Offensive"},
        new LookupEntry{ Id=1090, Name="Kirin Tor"},
        new LookupEntry{ Id=1091, Name="The Wyrmrest Accord"},
        new LookupEntry{ Id=1094, Name="The Silver Covenant"},
        new LookupEntry{ Id=1098, Name="Knights of the Ebon Blade"},
        new LookupEntry{ Id=1104, Name="Frenzyheart Tribe"},
        new LookupEntry{ Id=1105, Name="The Oracles"},
        new LookupEntry{ Id=1117, Name="CTF Flag - Alliance"},
        new LookupEntry{ Id=1118, Name="CTF Flag - Horde"},
        new LookupEntry{ Id=1120, Name="Mount - Taxi - Alliance"},
        new LookupEntry{ Id=1121, Name="Mount - Taxi - Horde"},
        new LookupEntry{ Id=1122, Name="Mount - Taxi - Neutral"},
        new LookupEntry{ Id=1124, Name="The Sunreavers"},
        new LookupEntry{ Id=1156, Name="The Ashen Verdict"},
        new LookupEntry{ Id=1168, Name="Knockback"}
    };
        }



        private List<LookupRow> RunLookup(LookupKind kind, string term)
        {
            var results = new List<LookupRow>();
            if (term == null) term = string.Empty;


            // Use cached faction list if available (fast + avoids repeated DB hits)
            if (kind == LookupKind.FactionTemplate
                && _factionCache != null
                && _factionCache.Count > 0)

            {
                string t = term.Trim();
                bool has = t.Length >= 2;
                bool idq = long.TryParse(t, out long qid);

                var filtered = new List<LookupRow>();

                if (idq)
                {
                    for (int i = 0; i < _factionCache.Count; i++)
                    {
                        if (_factionCache[i].Id == qid)
                        {
                            filtered.Add(_factionCache[i]);
                            break;
                        }
                    }
                }
                else if (has)
                {
                    string low = t.ToLowerInvariant();
                    for (int i = 0; i < _factionCache.Count; i++)
                    {
                        var row = _factionCache[i];
                        if (!string.IsNullOrEmpty(row.Name) && row.Name.ToLowerInvariant().Contains(low))
                        {
                            filtered.Add(row);
                            if (filtered.Count >= 200) break;
                        }
                        else if (row.Id.ToString(CultureInfo.InvariantCulture).Contains(low))
                        {
                            filtered.Add(row);
                            if (filtered.Count >= 200) break;
                        }
                    }
                }
                else
                {
                    // empty search: show first 200
                    for (int i = 0; i < _factionCache.Count && filtered.Count < 200; i++)
                        filtered.Add(_factionCache[i]);
                }

                return filtered;
            }

            // allow empty search to show a few entries
            bool hasText = term.Length >= 2;
            bool isId = long.TryParse(term, out long id);

            string table;
            string idCol;
            string nameCol;
            string where;
            var cmdText = "";

            switch (kind)
            {
                case LookupKind.Item:
                    table = "item_template";
                    idCol = "entry";
                    nameCol = "name";
                    break;
                case LookupKind.Creature:
                    table = "creature_template";
                    idCol = "entry";
                    nameCol = "name";
                    break;
                case LookupKind.GameObject:
                    table = "gameobject_template";
                    idCol = "entry";
                    nameCol = "name";
                    break;
                case LookupKind.Quest:
                    table = "quest_template";
                    idCol = "ID";
                    nameCol = "LogTitle";
                    break;
                default:
                    // Faction lookups vary by AzerothCore DB:
                    // - Some setups have dbc tables like faction_dbc / factiontemplate_dbc with localized names.
                    // - Others only have world.faction_template (numeric fields only).
                    // We'll auto-detect the best available source.
                    (table, idCol, nameCol) = GetBestFactionLookupSource();
                    break;
            }

            if (isId)
            {
                where = string.Format("`{0}` = @id", idCol);
            }
            else if (hasText)
            {
                if (kind == LookupKind.FactionTemplate)
                {
                    // Faction sources can be numeric-only; safe to always CAST.
                    where = string.Format("CAST(`{0}` AS CHAR) LIKE @like OR CAST(`{1}` AS CHAR) LIKE @like", idCol, nameCol);
                }
                else
                {
                    where = string.Format("CAST(`{0}` AS CHAR) LIKE @like OR `{1}` LIKE @like", idCol, nameCol);
                }
            }
            else
            {
                where = "1=1";
            }

            if (kind == LookupKind.Quest && !isId)
            {
                // For quests, sorting by title is much nicer when searching.
                cmdText = string.Format("SELECT `{0}` AS Id, `{1}` AS Name FROM `{2}` WHERE {3} ORDER BY `{1}` ASC LIMIT 200;", idCol, nameCol, table, where);
            }
            else
            {
                cmdText = string.Format("SELECT `{0}` AS Id, `{1}` AS Name FROM `{2}` WHERE {3} ORDER BY `{0}` DESC LIMIT 200;", idCol, nameCol, table, where);
            }

            using (var conn = new MySqlConnection(BuildConnString()))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(cmdText, conn))
                {
                    if (isId)
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                    }
                    else if (hasText)
                    {
                        cmd.Parameters.AddWithValue("@like", "%" + term + "%");
                    }

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            results.Add(new LookupRow
                            {
                                Id = r.IsDBNull(0) ? 0 : Convert.ToInt64(r.GetValue(0)),
                                Name = r.IsDBNull(1) ? "" : r.GetString(1)
                            });
                        }
                    }
                }
            }

            return results;
        }

        private (string table, string idCol, string nameCol) GetBestFactionLookupSource()
        {
            try
            {
                using (var conn = new MySqlConnection(BuildConnString()))
                {
                    conn.Open();
                    return GetBestFactionLookupSource(conn);
                }
            }
            catch
            {
                return ("faction_template", "ID", "faction");
            }
        }

        private (string table, string idCol, string nameCol) GetBestFactionLookupSource(MySqlConnection openConn)
        {
            // Prefer DBC-derived tables with readable names if present.
            // AzerothCore commonly has faction_dbc with columns like mname_lang_1.
            try
            {
                // 1) faction_dbc (best)
                if (TableExists(openConn, "faction_dbc"))
                {
                    var factionDbcNameCol =
                        FindFirstColumnLike(openConn, "faction_dbc", "mname_lang_%")
                        ?? FindFirstColumnLike(openConn, "faction_dbc", "mname_lang%")
                        ?? FindFirstColumnLike(openConn, "faction_dbc", "Name_Lang%")
                        ?? FindFirstColumnLike(openConn, "faction_dbc", "Name%")
                        ?? FindFirstColumnLike(openConn, "faction_dbc", "name%");

                    if (!string.IsNullOrEmpty(factionDbcNameCol))
                        return ("faction_dbc", "ID", factionDbcNameCol);
                }

                // 2) factiontemplate_dbc (optional)
                if (TableExists(openConn, "factiontemplate_dbc"))
                {
                    // Many builds do not include a readable name here; if a name-like column exists, use it.
                    var ftName =
                        FindFirstColumnLike(openConn, "factiontemplate_dbc", "Name%")
                        ?? FindFirstColumnLike(openConn, "factiontemplate_dbc", "name%")
                        ?? FindFirstColumnLike(openConn, "factiontemplate_dbc", "mname_lang_%")
                        ?? FindFirstColumnLike(openConn, "factiontemplate_dbc", "mname_lang%");

                    if (!string.IsNullOrEmpty(ftName))
                        return ("factiontemplate_dbc", "ID", ftName);

                    // If no readable name column exists, allow numeric-only searching.
                    return ("factiontemplate_dbc", "ID", "Faction");
                }

                // 3) faction_template (AzerothCore world table)
                if (TableExists(openConn, "faction_template"))
                {
                    // No name column in most schemas; use ID and faction as numeric search targets.
                    return ("faction_template", "ID", "faction");
                }
            }
            catch
            {
                // ignore and fall through
            }

            return ("faction_template", "ID", "faction");
        }

        // ===================== DB COLUMN HELPERS =====================

        private bool TableExists(MySqlConnection conn, string table)
        {
            using (var cmd = new MySqlCommand(
                       "SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @t LIMIT 1;",
                       conn))
            {
                cmd.Parameters.AddWithValue("@t", table);
                return cmd.ExecuteScalar() != null;
            }
        }

        private bool ColumnExists(MySqlConnection conn, string table, string column)
        {
            using (var cmd = new MySqlCommand(
                       "SELECT 1 FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @t AND column_name = @c LIMIT 1;",
                       conn))
            {
                cmd.Parameters.AddWithValue("@t", table);
                cmd.Parameters.AddWithValue("@c", column);
                return cmd.ExecuteScalar() != null;
            }
        }

        private string FindFirstColumnLike(MySqlConnection conn, string table, string likePattern)
        {
            using (var cmd = new MySqlCommand(
                       "SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @t AND column_name LIKE @p ORDER BY ordinal_position LIMIT 1;",
                       conn))
            {
                cmd.Parameters.AddWithValue("@t", table);
                cmd.Parameters.AddWithValue("@p", likePattern);
                var v = cmd.ExecuteScalar();
                return v == null ? null : v.ToString();
            }
        }

        // ===================== QUEST SECTION VISIBILITY LOCK =====================
        // IMPORTANT: this must be INSIDE MainWindow, and it uses FindName so you do NOT
        // need code-behind fields like ReqItemRow2, RewardItemRow3, etc.
        private void LockQuestSectionsToSingleRow()
        {
            void Collapse(string elementName)
            {
                if (FindName(elementName) is FrameworkElement fe)
                    fe.Visibility = Visibility.Collapsed;
            }

            // Required Items (rows 2–6)
            Collapse("ReqItemRow2");
            Collapse("ReqItemRow3");
            Collapse("ReqItemRow4");
            Collapse("ReqItemRow5");
            Collapse("ReqItemRow6");

            // Reward Items (rows 2–4)
            Collapse("RewardItemRow2");
            Collapse("RewardItemRow3");
            Collapse("RewardItemRow4");

            // Choice Rewards (rows 2–6)
            Collapse("ChoiceItemRow2");
            Collapse("ChoiceItemRow3");
            Collapse("ChoiceItemRow4");
            Collapse("ChoiceItemRow5");
            Collapse("ChoiceItemRow6");

            // Required NPC/GO (rows 2–4)
            Collapse("ReqNpcGoRow2");
            Collapse("ReqNpcGoRow3");
            Collapse("ReqNpcGoRow4");

            // Reputation Rewards (rows 2–5)
            Collapse("RepFactionRow2");
            Collapse("RepFactionRow3");
            Collapse("RepFactionRow4");
            Collapse("RepFactionRow5");
        }
        private T FindFirstByName<T>(params string[] names) where T : class
        {
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (FindName(n) is T t) return t;
            }
            return null;
        }

        private void HookItemPreviewEvents()
        {
            // Attach events in code so the preview still updates even if XAML hookups drift.
            var nameBox = FindFirstByName<TextBox>(
                "ItemNameBox", "ItemNameTextBox", "ItemName", "ItemNameTxt", "ItemNameField");

            var displayBox = FindFirstByName<TextBox>(
                "ItemDisplayIdBox", "ItemDisplayIDBox", "ItemDisplayId", "ItemDisplayID",
                "DisplayIdBox", "DisplayIDBox", "ItemDisplayTextBox", "ItemDisplayIdTextBox");

            var qualityBox = FindFirstByName<ComboBox>(
                "ItemQualityBox", "ItemQuality", "ItemQualityCombo", "ItemQualityComboBox");

            var classBox = FindFirstByName<ComboBox>(
                "ItemClassBox", "ItemClass", "ItemClassCombo", "ItemClassComboBox");

            var subclassBox = FindFirstByName<ComboBox>(
                "ItemSubclassBox", "ItemSubclass", "ItemSubclassCombo", "ItemSubclassComboBox");

            var invBox = FindFirstByName<ComboBox>(
                "ItemInventoryTypeBox", "ItemInventoryType", "ItemEquipSlotBox", "ItemEquipSlot");

            if (nameBox != null) nameBox.TextChanged += ItemPreview_Changed;
            if (displayBox != null) displayBox.TextChanged += ItemPreview_Changed;
            if (qualityBox != null) qualityBox.SelectionChanged += ItemPreview_Changed;
            if (classBox != null) classBox.SelectionChanged += ItemPreview_Changed;
            if (subclassBox != null) subclassBox.SelectionChanged += ItemPreview_Changed;
            if (invBox != null) invBox.SelectionChanged += ItemPreview_Changed;
        }

        // ContainerSlots (Bag Slots) input helpers: digits only, clamp 0-255 (Trinity-like).
        private void ItemContainerSlotsBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Only digits
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void ItemContainerSlotsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ItemContainerSlotsBox == null)
                return;

            // Prevent recursion while rewriting Text
            ItemContainerSlotsBox.TextChanged -= ItemContainerSlotsBox_TextChanged;

            try
            {
                var raw = (ItemContainerSlotsBox.Text ?? string.Empty).Trim();

                if (raw.Length == 0)
                {
                    ItemContainerSlotsBox.Text = "0";
                }
                else if (int.TryParse(raw, out int value))
                {
                    if (value < 0) value = 0;
                    if (value > 255) value = 255;

                    var normalized = value.ToString(CultureInfo.InvariantCulture);
                    if (!string.Equals(ItemContainerSlotsBox.Text, normalized, StringComparison.Ordinal))
                        ItemContainerSlotsBox.Text = normalized;
                }
                else
                {
                    ItemContainerSlotsBox.Text = "0";
                }

                ItemContainerSlotsBox.CaretIndex = ItemContainerSlotsBox.Text.Length;
            }
            finally
            {
                ItemContainerSlotsBox.TextChanged += ItemContainerSlotsBox_TextChanged;
            }
        }

    } // <-- closes MainWindow
      // Keep LookupEntry OUTSIDE MainWindow (but still inside the namespace)
    public class LookupEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public override string ToString() => $"{Id} - {Name}";
    }
}
