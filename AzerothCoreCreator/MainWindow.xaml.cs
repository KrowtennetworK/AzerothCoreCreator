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
using Microsoft.Win32;


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

        // Cached AreaTable (zones) for QuestSortID dropdown (positive values)
        private List<LookupEntry> _areaTableZoneEntries = new List<LookupEntry>();
        private bool _areaTableLoaded = false;
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


        // ===================== EMOTE LOOKUP (hardcoded from Emotes.dbc export) =====================
        private sealed class EmoteEntry
        {
            public int Id { get; }
            public string Name { get; }
            public int AnimId { get; }
            public EmoteEntry(int id, string name, int animId) { Id = id; Name = name; AnimId = animId; }
            public override string ToString() => $"{Id} - {Name}";
        }

        private static readonly List<EmoteEntry> _emoteList = new List<EmoteEntry>
        {
            new EmoteEntry(0, "ONESHOT_NONE", 0),
            new EmoteEntry(1, "ONESHOT_TALK(DNR)", 60),
            new EmoteEntry(2, "ONESHOT_BOW", 66),
            new EmoteEntry(3, "ONESHOT_WAVE(DNR)", 67),
            new EmoteEntry(4, "ONESHOT_CHEER(DNR)", 68),
            new EmoteEntry(5, "ONESHOT_EXCLAMATION(DNR)", 64),
            new EmoteEntry(6, "ONESHOT_QUESTION", 65),
            new EmoteEntry(7, "ONESHOT_EAT", 61),
            new EmoteEntry(10, "STATE_DANCE", 69),
            new EmoteEntry(11, "ONESHOT_LAUGH", 70),
            new EmoteEntry(12, "STATE_SLEEP", 0),
            new EmoteEntry(13, "STATE_SIT", 0),
            new EmoteEntry(14, "ONESHOT_RUDE(DNR)", 73),
            new EmoteEntry(15, "ONESHOT_ROAR(DNR)", 74),
            new EmoteEntry(16, "ONESHOT_KNEEL", 75),
            new EmoteEntry(17, "ONESHOT_KISS", 76),
            new EmoteEntry(18, "ONESHOT_CRY", 77),
            new EmoteEntry(19, "ONESHOT_CHICKEN", 78),
            new EmoteEntry(20, "ONESHOT_BEG", 79),
            new EmoteEntry(21, "ONESHOT_APPLAUD", 80),
            new EmoteEntry(22, "ONESHOT_SHOUT(DNR)", 81),
            new EmoteEntry(23, "ONESHOT_FLEX", 82),
            new EmoteEntry(24, "ONESHOT_SHY(DNR)", 83),
            new EmoteEntry(25, "ONESHOT_POINT(DNR)", 84),
            new EmoteEntry(26, "STATE_STAND", 0),
            new EmoteEntry(27, "STATE_READYUNARMED", 25),
            new EmoteEntry(28, "STATE_WORK_SHEATHED", 62),
            new EmoteEntry(29, "STATE_POINT(DNR)", 84),
            new EmoteEntry(30, "STATE_NONE", 0),
            new EmoteEntry(33, "ONESHOT_WOUND", 9),
            new EmoteEntry(34, "ONESHOT_WOUNDCRITICAL", 10),
            new EmoteEntry(35, "ONESHOT_ATTACKUNARMED", 16),
            new EmoteEntry(36, "ONESHOT_ATTACK1H", 17),
            new EmoteEntry(37, "ONESHOT_ATTACK2HTIGHT", 18),
            new EmoteEntry(38, "ONESHOT_ATTACK2HLOOSE", 19),
            new EmoteEntry(39, "ONESHOT_PARRYUNARMED", 20),
            new EmoteEntry(43, "ONESHOT_PARRYSHIELD", 24),
            new EmoteEntry(44, "ONESHOT_READYUNARMED", 25),
            new EmoteEntry(45, "ONESHOT_READY1H", 26),
            new EmoteEntry(48, "ONESHOT_READYBOW", 29),
            new EmoteEntry(50, "ONESHOT_SPELLPRECAST", 31),
            new EmoteEntry(51, "ONESHOT_SPELLCAST", 32),
            new EmoteEntry(53, "ONESHOT_BATTLEROAR", 55),
            new EmoteEntry(54, "ONESHOT_SPECIALATTACK1H", 57),
            new EmoteEntry(60, "ONESHOT_KICK", 95),
            new EmoteEntry(61, "ONESHOT_ATTACKTHROWN", 107),
            new EmoteEntry(64, "STATE_STUN", 14),
            new EmoteEntry(65, "STATE_DEAD", 0),
            new EmoteEntry(66, "ONESHOT_SALUTE", 113),
            new EmoteEntry(68, "STATE_KNEEL", 0),
            new EmoteEntry(69, "STATE_USESTANDING", 63),
            new EmoteEntry(70, "ONESHOT_WAVE_NOSHEATHE", 67),
            new EmoteEntry(71, "ONESHOT_CHEER_NOSHEATHE", 68),
            new EmoteEntry(92, "ONESHOT_EAT_NOSHEATHE", 199),
            new EmoteEntry(93, "STATE_STUN_NOSHEATHE", 137),
            new EmoteEntry(94, "ONESHOT_DANCE", 69),
            new EmoteEntry(113, "ONESHOT_SALUTE_NOSHEATH", 210),
            new EmoteEntry(133, "STATE_USESTANDING_NOSHEATHE", 138),
            new EmoteEntry(153, "ONESHOT_LAUGH_NOSHEATHE", 70),
            new EmoteEntry(173, "STATE_WORK", 136),
            new EmoteEntry(193, "STATE_SPELLPRECAST", 31),
            new EmoteEntry(213, "ONESHOT_READYRIFLE", 48),
            new EmoteEntry(214, "STATE_READYRIFLE", 48),
            new EmoteEntry(233, "STATE_WORK_MINING", 136),
            new EmoteEntry(234, "STATE_WORK_CHOPWOOD", 136),
            new EmoteEntry(253, "STATE_APPLAUD", 80),
            new EmoteEntry(254, "ONESHOT_LIFTOFF", 192),
            new EmoteEntry(273, "ONESHOT_YES(DNR)", 185),
            new EmoteEntry(274, "ONESHOT_NO(DNR)", 186),
            new EmoteEntry(275, "ONESHOT_TRAIN(DNR)", 195),
            new EmoteEntry(293, "ONESHOT_LAND", 200),
            new EmoteEntry(313, "STATE_AT_EASE", 0),
            new EmoteEntry(333, "STATE_READY1H", 26),
            new EmoteEntry(353, "STATE_SPELLKNEELSTART", 140),
            new EmoteEntry(373, "STAND_STATE_SUBMERGED", 202),
            new EmoteEntry(374, "ONESHOT_SUBMERGE", 201),
            new EmoteEntry(375, "STATE_READY2H", 27),
            new EmoteEntry(376, "STATE_READYBOW", 29),
            new EmoteEntry(377, "ONESHOT_MOUNTSPECIAL", 94),
            new EmoteEntry(378, "STATE_TALK", 60),
            new EmoteEntry(379, "STATE_FISHING", 134),
            new EmoteEntry(380, "ONESHOT_FISHING", 133),
            new EmoteEntry(381, "ONESHOT_LOOT", 50),
            new EmoteEntry(382, "STATE_WHIRLWIND", 126),
            new EmoteEntry(383, "STATE_DROWNED", 132),
            new EmoteEntry(384, "STATE_HOLD_BOW", 109),
            new EmoteEntry(385, "STATE_HOLD_RIFLE", 110),
            new EmoteEntry(386, "STATE_HOLD_THROWN", 111),
            new EmoteEntry(387, "ONESHOT_DROWN", 131),
            new EmoteEntry(388, "ONESHOT_STOMP", 181),
            new EmoteEntry(389, "ONESHOT_ATTACKOFF", 87),
            new EmoteEntry(390, "ONESHOT_ATTACKOFFPIERCE", 88),
            new EmoteEntry(391, "STATE_ROAR", 74),
            new EmoteEntry(392, "STATE_LAUGH", 70),
            new EmoteEntry(393, "ONESHOT_CREATURE_SPECIAL", 130),
            new EmoteEntry(394, "ONESHOT_JUMPLANDRUN", 187),
            new EmoteEntry(395, "ONESHOT_JUMPEND", 39),
            new EmoteEntry(396, "ONESHOT_TALK_NOSHEATHE", 208),
            new EmoteEntry(397, "ONESHOT_POINT_NOSHEATHE", 209),
            new EmoteEntry(398, "STATE_CANNIBALIZE", 203),
            new EmoteEntry(399, "ONESHOT_JUMPSTART", 37),
            new EmoteEntry(400, "STATE_DANCESPECIAL", 211),
            new EmoteEntry(401, "ONESHOT_DANCESPECIAL", 211),
            new EmoteEntry(402, "EYEBEAM_DH", 146),
            new EmoteEntry(403, "GLIDE_DH", 214),
            new EmoteEntry(404, "VENGEFUL_DH", 215),
            new EmoteEntry(405, "DOUBLE_JUMP", 216),
            new EmoteEntry(406, "CHAOS_STRIKE", 217),
            new EmoteEntry(407, "BLADE_DANCE_A", 218),
            new EmoteEntry(408, "BLADE_DANCE_B", 219),
            new EmoteEntry(409, "BLADE_DANCE_C", 220),
            new EmoteEntry(410, "BLADE_DANCE_D", 221),
            new EmoteEntry(411, "ONESHOT_CUSTOMSPELL10", 222),
            new EmoteEntry(412, "STATE_EXCLAIM", 64),
            new EmoteEntry(413, "STATE_DANCE_CUSTOM", 42),
            new EmoteEntry(415, "STATE_SIT_CHAIR_MED", 103),
            new EmoteEntry(416, "STATE_CUSTOM_SPELL_01", 213),
            new EmoteEntry(417, "STATE_CUSTOM_SPELL_02", 214),
            new EmoteEntry(418, "STATE_EAT", 61),
            new EmoteEntry(419, "BLADE_DANCE_E", 216),
            new EmoteEntry(420, "BLADE_DANCE_F", 215),
            new EmoteEntry(421, "STATE_CUSTOM_SPELL_05", 217),
            new EmoteEntry(422, "STATE_SPELLEFFECT_HOLD", 158),
            new EmoteEntry(423, "STATE_EAT_NO_SHEATHE", 199),
            new EmoteEntry(424, "STATE_MOUNT", 91),
            new EmoteEntry(425, "STATE_READY2HL", 28),
            new EmoteEntry(426, "STATE_SIT_CHAIR_HIGH", 104),
            new EmoteEntry(427, "STATE_FALL", 40),
            new EmoteEntry(428, "STATE_LOOT", 188),
            new EmoteEntry(429, "STATE_SUBMERGED", 202),
            new EmoteEntry(430, "ONESHOT_COWER(DNR)", 225),
            new EmoteEntry(431, "STATE_COWER", 225),
            new EmoteEntry(432, "ONESHOT_USESTANDING", 63),
            new EmoteEntry(433, "STATE_STEALTH_STAND", 120),
            new EmoteEntry(434, "ONESHOT_OMNICAST_GHOUL (W/SOUND", 54),
            new EmoteEntry(435, "ONESHOT_ATTACKBOW", 46),
            new EmoteEntry(436, "ONESHOT_ATTACKRIFLE", 49),
            new EmoteEntry(437, "STATE_SWIM_IDLE", 41),
            new EmoteEntry(438, "STATE_ATTACK_UNARMED", 16),
            new EmoteEntry(439, "ONESHOT_SPELLCAST (W/SOUND)", 32),
            new EmoteEntry(440, "ONESHOT_DODGE", 30),
            new EmoteEntry(441, "ONESHOT_PARRY1H", 21),
            new EmoteEntry(442, "ONESHOT_PARRY2H", 22),
            new EmoteEntry(443, "ONESHOT_PARRY2HL", 28),
            new EmoteEntry(444, "STATE_FLYFALL", 269),
            new EmoteEntry(445, "ONESHOT_FLYDEATH", 230),
            new EmoteEntry(446, "STATE_FLY_FALL", 269),
            new EmoteEntry(447, "ONESHOT_FLY_SIT_GROUND_DOWN", 325),
            new EmoteEntry(448, "ONESHOT_FLY_SIT_GROUND_UP", 327),
            new EmoteEntry(449, "ONESHOT_EMERGE", 224),
            new EmoteEntry(450, "ONESHOT_DRAGONSPIT", 182),
            new EmoteEntry(451, "STATE_SPECIALUNARMED", 118),
            new EmoteEntry(452, "ONESHOT_FLYGRAB", 455),
            new EmoteEntry(453, "STATE_FLYGRABCLOSED", 456),
            new EmoteEntry(454, "ONESHOT_FLYGRABTHROWN", 457),
            new EmoteEntry(455, "STATE_FLY_SIT_GROUND", 326),
            new EmoteEntry(456, "STATE_WALKBACKWARDS", 13),
            new EmoteEntry(457, "ONESHOT_FLYTALK", 289),
            new EmoteEntry(458, "ONESHOT_FLYATTACK1H", 246),
            new EmoteEntry(459, "STATE_CUSTOMSPELL08", 220),
            new EmoteEntry(460, "ONESHOT_FLY_DRAGONSPIT", 411),
            new EmoteEntry(461, "STATE_SIT_CHAIR_LOW", 102),
            new EmoteEntry(462, "ONE_SHOT_STUN", 14),
            new EmoteEntry(463, "ONESHOT_SPELLCAST_OMNI", 54),
            new EmoteEntry(465, "STATE_READYTHROWN", 108),
            new EmoteEntry(466, "ONESHOT_WORK_CHOPWOOD", 62),
            new EmoteEntry(467, "ONESHOT_WORK_MINING", 62),
            new EmoteEntry(468, "STATE_SPELL_CHANNEL_OMNI", 125),
            new EmoteEntry(469, "STATE_SPELL_CHANNEL_DIRECTED", 124),
            new EmoteEntry(470, "STAND_STATE_NONE", 0),
            new EmoteEntry(471, "STATE_READYJOUST", 476),
            new EmoteEntry(473, "STATE_STRANGULATE", 474),
            new EmoteEntry(474, "STATE_READYSPELLOMNI", 52),
            new EmoteEntry(475, "STATE_HOLD_JOUST", 478),
            new EmoteEntry(476, "ONESHOT_CRY (JAINA PROUDMOORE ONLY)", 77),
            new EmoteEntry(477, "FELRUSH_DH", 506),
            new EmoteEntry(478, "BLADE_DANCE_A", 600)
        };

        public MainWindow()
        {
            InitializeComponent();
            UpdateUninstallDisplaySize();
            EnsureFlagCheckboxesBuilt();
            UpdateCreatureOptionalSectionsVisibility();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateCreatureCombos();
            PopulateItemCombos();
            EnsureAreaTableLoaded();
            PopulateQuestDefaults();
            UpdateQuestPreview();
            EnsureFlagCheckboxesBuilt();
            UpdateCreatureOptionalSectionsVisibility();
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


        // Loads AreaTable zones for QuestSortID (positive values). Hardcoded fallback (no external file required).
        private void EnsureAreaTableLoaded()
        {
            if (_areaTableLoaded) return;
            _areaTableLoaded = true;

            try
            {
                // Optional override: if AreaTable.csv exists next to the EXE, prefer it.
                string externalPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AreaTable.csv");
                string csvText = null;

                if (System.IO.File.Exists(externalPath))
                {
                    csvText = System.IO.File.ReadAllText(externalPath);
                }
                else
                {
                    // Embedded gzipped CSV (generated from your AreaTable.csv)
                    const string AREA_TABLE_GZ_B64 = "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";
                    byte[] gz = Convert.FromBase64String(AREA_TABLE_GZ_B64);
                    using var ms = new System.IO.MemoryStream(gz);
                    using var gzs = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    using var sr = new System.IO.StreamReader(gzs);
                    csvText = sr.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(csvText))
                    return;

                var zones = new List<LookupEntry>();
                using (var reader = new System.IO.StringReader(csvText))
                {
                    string header = reader.ReadLine();
                    if (header == null) return;

                    // Find column indexes for ID and enUS name
                    var headerCols = SplitCsvLine(header);
                    int idIdx = headerCols.FindIndex(c => string.Equals(c, "ID", StringComparison.OrdinalIgnoreCase));
                    int nameIdx = headerCols.FindIndex(c => string.Equals(c, "AreaName_Lang_enUS", StringComparison.OrdinalIgnoreCase)
                                                       || string.Equals(c, "AreaName", StringComparison.OrdinalIgnoreCase));
                    if (idIdx < 0 || nameIdx < 0) return;

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var cols = SplitCsvLine(line);
                        if (cols.Count <= Math.Max(idIdx, nameIdx)) continue;

                        if (!int.TryParse(cols[idIdx], out int id)) continue;
                        string name = cols[nameIdx]?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        // Only keep real zone-like entries (we still allow everything; user can search)
                        zones.Add(new LookupEntry { Id = id, Name = name });
                    }
                }

                _areaTableZoneEntries = zones
                    .GroupBy(z => z.Id)
                    .Select(g => g.First())
                    .OrderBy(z => z.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                // Swallow errors; QuestSort will still work for categories (negative values).
            }
        }

        // Minimal CSV splitter that respects quotes.
        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null) return result;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];

                if (ch == '"')
                {
                    // handle escaped quote
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }

            result.Add(sb.ToString());
            return result;
        }


        private void PopulateQuestDefaults()
        {
            QuestMinLevelBox.Text = "1";
            QuestLevelBox.Text = "1";
            QuestRewardXpBox.Text = "0";
            QuestRewardMoneyBox.Text = "0";
            QuestSortBox.Text = "0";

            // Quest Info dropdown (QuestInfoID) - categories from QuestInfo.dbc
            if (QuestInfoCombo != null && QuestInfoCombo.Items.Count == 0)
            {
                void AddInfo(int id, string name)
                {
                    QuestInfoCombo.Items.Add(
                        new ComboBoxItem
                        {
                            Content = $"{id} - {name}",
                            Tag = id
                        });
                }

                AddInfo(0, "None");
                AddInfo(1, "Group");
                AddInfo(21, "Life");
                AddInfo(41, "PvP");
                AddInfo(62, "Raid");
                AddInfo(81, "Dungeon");
                AddInfo(82, "World Event");
                AddInfo(83, "Legendary");
                AddInfo(84, "Escort");
                AddInfo(85, "Heroic");
                AddInfo(88, "Raid (10)");
                AddInfo(89, "Raid (25)");

                QuestInfoCombo.SelectedIndex = 0;
            }

            // Quest Type dropdown (QuestType) - core enable/disable/auto-complete
            if (QuestQuestTypeCombo != null && QuestQuestTypeCombo.Items.Count == 0)
            {
                void AddQType(int id, string name)
                {
                    QuestQuestTypeCombo.Items.Add(new ComboBoxItem { Content = $"{id} - {name}", Tag = id });
                }

                AddQType(0, "Auto-complete on accept");
                AddQType(1, "Disabled (not implemented)");
                AddQType(2, "Enabled (normal)");

                QuestQuestTypeCombo.SelectedIndex = 2; // default to enabled
            }

            // Quest Sort dropdown helper (QuestSortID)
            // QuestSortID is now entered manually (or via Find button) to avoid UI lag from huge dropdowns.
        }

        private bool _flagsBuilt = false;

        private void EnsureFlagCheckboxesBuilt()
        {
            if (_flagsBuilt) return;

            // Use FindName to avoid any name-scope surprises if layout changes later
            NpcFlagWrap = (WrapPanel)(this.FindName("NpcFlagWrap") ?? NpcFlagWrap);
            UnitFlagsWrap = (WrapPanel)(this.FindName("UnitFlagsWrap") ?? UnitFlagsWrap);
            UnitFlags2Wrap = (WrapPanel)(this.FindName("UnitFlags2Wrap") ?? UnitFlags2Wrap);
            FlagsExtraWrap = (WrapPanel)(this.FindName("FlagsExtraWrap") ?? FlagsExtraWrap);

            if (NpcFlagWrap == null || UnitFlagsWrap == null || UnitFlags2Wrap == null || FlagsExtraWrap == null)
                return;

            BuildFlagCheckboxes();
            _flagsBuilt = true;
        }


        private static void SelectComboItemByTagInt(ComboBox combo, int tagValue, int fallbackIndex = 0)
        {
            if (combo == null) return;

            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem cbi && cbi.Tag is int t && t == tagValue)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            if (combo.Items.Count > 0 && fallbackIndex >= 0 && fallbackIndex < combo.Items.Count)
                combo.SelectedIndex = fallbackIndex;
        }




        // Toggle optional Creature sections (Spawn + Speech) from their checkboxes
        private void UpdateCreatureOptionalSectionsVisibility()
        {
            var spawnGrid = this.FindName("CreatureSpawnFieldsGrid") as FrameworkElement;
            var speechPanel = this.FindName("SpeechFieldsPanel") as FrameworkElement;

            var spawnEnable = this.FindName("CreatureSpawnEnable") as CheckBox;
            var speechEnable = this.FindName("SpeechEnable") as CheckBox;

            if (spawnGrid != null && spawnEnable != null)
                spawnGrid.Visibility = (spawnEnable.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (speechPanel != null && speechEnable != null)
                speechPanel.Visibility = (speechEnable.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CreatureSpawnEnable_Checked(object sender, RoutedEventArgs e) => UpdateCreatureOptionalSectionsVisibility();
        private void CreatureSpawnEnable_Unchecked(object sender, RoutedEventArgs e) => UpdateCreatureOptionalSectionsVisibility();

        private void SpeechEnable_Checked(object sender, RoutedEventArgs e) => UpdateCreatureOptionalSectionsVisibility();
        private void SpeechEnable_Unchecked(object sender, RoutedEventArgs e) => UpdateCreatureOptionalSectionsVisibility();
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

        private void UpdateUninstallDisplaySize()
        {
            try
            {
                string installPath = AppContext.BaseDirectory;

                long bytes = GetDirectorySize(new DirectoryInfo(installPath));
                int sizeKb = (int)(bytes / 1024);

                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KrowtennetworkK.AzerothCoreCreator",
                    writable: true);

                if (key != null)
                {
                    key.SetValue("DisplaySize", sizeKb, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Cosmetic only
            }
        }

        private long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;

            foreach (var file in dir.GetFiles())
                size += file.Length;

            foreach (var sub in dir.GetDirectories())
                size += GetDirectorySize(sub);

            return size;
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

        private void FindEmote_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Window
                {
                    Title = "Find Emote",
                    Width = 620,
                    Height = 700,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var root = new DockPanel { Margin = new Thickness(10) };

                var searchBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                searchBox.ToolTip = "Search by ID or name (example: dance, dead, 65)";
                DockPanel.SetDock(searchBox, Dock.Top);
                root.Children.Add(searchBox);

                var list = new ListView { Margin = new Thickness(0, 0, 0, 8) };

                var gv = new GridView();
                gv.Columns.Add(new GridViewColumn { Header = "ID", DisplayMemberBinding = new System.Windows.Data.Binding("Id"), Width = 70 });
                gv.Columns.Add(new GridViewColumn { Header = "Emote", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 430 });
                gv.Columns.Add(new GridViewColumn { Header = "AnimID", DisplayMemberBinding = new System.Windows.Data.Binding("AnimId"), Width = 80 });
                list.View = gv;

                list.ItemsSource = _emoteList;
                root.Children.Add(list);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var ok = new Button { Content = "Use Selected", Width = 120, Margin = new Thickness(0, 0, 8, 0) };
                var cancel = new Button { Content = "Cancel", Width = 90 };

                buttons.Children.Add(ok);
                buttons.Children.Add(cancel);

                DockPanel.SetDock(buttons, Dock.Bottom);
                root.Children.Add(buttons);

                win.Content = root;

                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(list.ItemsSource);
                view.Filter = o =>
                {
                    if (o is not EmoteEntry ee) return false;

                    string q = (searchBox.Text ?? "").Trim();
                    if (q.Length == 0) return true;

                    if (int.TryParse(q, out int qid))
                        return ee.Id == qid;

                    return ee.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                };

                searchBox.TextChanged += (s, ev) => view.Refresh();

                void accept()
                {
                    if (list.SelectedItem is EmoteEntry ee)
                    {
                        SpeechEmoteBox.Text = ee.Id.ToString(CultureInfo.InvariantCulture);
                        win.DialogResult = true;
                        win.Close();
                    }
                }

                ok.Click += (s, ev) => accept();
                cancel.Click += (s, ev) => { win.DialogResult = false; win.Close(); };

                list.MouseDoubleClick += (s, ev) => accept();
                list.KeyDown += (s, ev) =>
                {
                    if (ev.Key == Key.Enter)
                    {
                        accept();
                        ev.Handled = true;
                    }
                };

                searchBox.KeyDown += (s, ev) =>
                {
                    if (ev.Key == Key.Down)
                    {
                        list.Focus();
                        if (list.Items.Count > 0 && list.SelectedIndex < 0)
                            list.SelectedIndex = 0;
                        ev.Handled = true;
                    }
                };

                // Preselect current emote if possible
                if (int.TryParse(SpeechEmoteBox.Text, out int currentId))
                {
                    var found = _emoteList.FirstOrDefault(x => x.Id == currentId);
                    if (found != null)
                        list.SelectedItem = found;
                }

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Find Emote failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            // Stats
            int rank = ComboTagInt(CreatureRankCombo);
            int dmgSchool = ComboTagInt(CreatureDamageSchoolCombo);
            int unitClass = ComboTagInt(CreatureUnitClassCombo);

            int baseAttackTime = ParseInt(CreatureBaseAttackTimeBox?.Text ?? "1500", 1500);
            int rangeAttackTime = ParseInt(CreatureRangeAttackTimeBox?.Text ?? "2000", 2000);

            int racialLeader = (CreatureRacialLeaderCheck?.IsChecked == true) ? 1 : 0;
            int regenHealth = (CreatureRegenHealthCheck?.IsChecked == true) ? 1 : 0;

            int minHealth = ParseInt(CreatureMinHealthBox?.Text ?? "0", 0);
            int maxHealth = ParseInt(CreatureMaxHealthBox?.Text ?? "0", 0);
            int minMana = ParseInt(CreatureMinManaBox?.Text ?? "0", 0);
            int maxMana = ParseInt(CreatureMaxManaBox?.Text ?? "0", 0);

            double minDmg = ParseDouble(CreatureMinDmgBox?.Text ?? "0", 0);
            double maxDmg = ParseDouble(CreatureMaxDmgBox?.Text ?? "0", 0);
            double minRangedDmg = ParseDouble(CreatureMinRangedDmgBox?.Text ?? "0", 0);
            double maxRangedDmg = ParseDouble(CreatureMaxRangedDmgBox?.Text ?? "0", 0);

            int attackPower = ParseInt(CreatureAttackPowerBox?.Text ?? "0", 0);
            int rangedAttackPower = ParseInt(CreatureRangedAttackPowerBox?.Text ?? "0", 0);
            int armor = ParseInt(CreatureArmorBox?.Text ?? "0", 0);

            bool isCivilian = (CreatureCivilianCheck?.IsChecked == true);

            // Equipment (creature_equip_template): ItemID1/2/3 (weapon1/weapon2/ranged)
            int equipItem1 = ParseInt(CreatureEquipItem1Box?.Text ?? "0", 0);
            int equipItem2 = ParseInt(CreatureEquipItem2Box?.Text ?? "0", 0);
            int equipItem3 = ParseInt(CreatureEquipItem3Box?.Text ?? "0", 0);
            int equipmentId = (equipItem1 > 0 || equipItem2 > 0 || equipItem3 > 0) ? 1 : 0;

            uint npcflag = SumFlags(_npcFlags);
            uint unitFlags = SumFlags(_unitFlags);
            uint unitFlags2 = SumFlags(_unitFlags2);
            uint unitFlags2Adjusted = unitFlags2;
            if (isCivilian) unitFlags2Adjusted |= 0x00000001; // UNIT_FLAG2_CIVILIAN
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
            sb.Append("(`entry`,`name`,`subname`,`minlevel`,`maxlevel`,`faction`,`npcflag`,`unit_flags`,`unit_flags2`,`type`,`family`,`flags_extra`,`modelid1`,`equipment_id`,`rank`,`dmgschool`,`baseattacktime`,`rangeattacktime`,`unit_class`,`racialleader`,`regeneratehealth`,`minhealth`,`maxhealth`,`minmana`,`maxmana`,`mindmg`,`maxdmg`,`minrangedmg`,`maxrangedmg`,`attackpower`,`rangedattackpower`,`armor`) VALUES ");
            sb.AppendFormat("(@ENTRY,'{0}','{1}',{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28},{29},{30},{31});",
                name, subname, minLevel, maxLevel, faction, npcflag, unitFlags, unitFlags2Adjusted, creatureType, family, flagsExtra, displayId, equipmentId,
                rank, dmgSchool, baseAttackTime, rangeAttackTime, unitClass, racialLeader, regenHealth,
                minHealth, maxHealth, minMana, maxMana,
                minDmg.ToString(CultureInfo.InvariantCulture), maxDmg.ToString(CultureInfo.InvariantCulture),
                minRangedDmg.ToString(CultureInfo.InvariantCulture), maxRangedDmg.ToString(CultureInfo.InvariantCulture),
                attackPower, rangedAttackPower, armor);
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

            // Equipment optional (creature_equip_template)
            if (equipmentId > 0)
            {
                sb.AppendLine("-- Equipment (creature_equip_template)");
                sb.AppendLine("DELETE FROM `creature_equip_template` WHERE `CreatureID`=@ENTRY AND `ID`=1;");
                sb.Append("INSERT INTO `creature_equip_template` ");
                sb.Append("(`CreatureID`,`ID`,`ItemID1`,`ItemID2`,`ItemID3`) VALUES ");
                sb.AppendFormat("(@ENTRY,1,{0},{1},{2});", equipItem1, equipItem2, equipItem3);
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

            int questInfoId = GetSelectedComboTagInt(QuestQuestTypeCombo, 0);
            int questType = GetSelectedComboTagInt(QuestQuestTypeCombo, 2);
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
            sb.Append("(`ID`,`QuestType`,`LogTitle`,`QuestDescription`,`Objectives`,`MinLevel`,`QuestLevel`,`QuestInfoID`,`QuestSortID`,`Flags`,`AllowableRaces`,");
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
            sb.AppendFormat("@ID,{0},'{1}','{2}','{3}',{4},{5},{6},{7},{8},{9},{10},{11},",
                questType, title, details, objectives, minLevel, questLevel, questInfoId, qSort, questFlags, allowableRaces, rewardXp, rewardMoney);

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
            // QuestSortID rules:
            //  - 0: none
            //  - >0: AreaTable (zone) ID
            //  - <0: -QuestSort.dbc ID (category)
            var combined = new List<LookupEntry>();
            combined.Add(new LookupEntry { Id = 0, Name = "None" });

            // Zones (positive)
            if (_areaTableZoneEntries != null && _areaTableZoneEntries.Count > 0)
                combined.AddRange(_areaTableZoneEntries.Select(z => new LookupEntry { Id = z.Id, Name = $"Zone: {z.Name}" }));

            // Sort categories (negative)
            if (_questSortEntries != null && _questSortEntries.Count > 0)
                combined.AddRange(_questSortEntries.Select(s => new LookupEntry { Id = -Math.Abs(s.Id), Name = $"Sort: {s.Name}" }));

            OpenSimpleListLookupWindow(
                title: "Find Quest Sort / Zone (QuestSortID)",
                entries: combined,
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

        // ===================== CREATURE EQUIPMENT LOOKUP =====================
        private TextBox _creatureEquipLookupTarget;

        private void CreatureEquipLookupTarget_GotFocus(object sender, RoutedEventArgs e)
        {
            _creatureEquipLookupTarget = sender as TextBox;
        }

        private void CreatureFindEquipment_Click(object sender, RoutedEventArgs e)
        {
            OpenLookupWindow(LookupKind.Item, _creatureEquipLookupTarget);
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
