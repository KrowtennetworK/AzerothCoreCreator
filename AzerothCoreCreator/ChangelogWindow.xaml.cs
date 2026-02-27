using System.Windows;

namespace AzerothCoreCreator
{
    public partial class ChangelogWindow : Window
    {
        public string TitleText { get; set; }
        public string ChangelogText { get; set; }

        public ChangelogWindow(string currentVersion)
        {
            InitializeComponent();

            TitleText = $"AzerothCore Creator v{currentVersion} - Changelog";

            // Update this text when you release. Newest entries at top.
            ChangelogText =
@"v0.4.0
- Added WoW Font to Quest Preview
- Added Incomplete Quest Text Field to Quest Tab
- Resized Default dimensions of the app

v0.3.0
-  Added Dressing Room Tab

v0.2.0
- Fixed Double Changelog Popup
- Added Item Quote field to Item Tab
- Added Item Sockets section to Item tab
- Added Item sockets to Item Preview tab
- Added Socket Images to Item preview
- Added Quest background image to Quest Preview
- Added Gold/Silver/Bronze Images

v0.1.9
- Added Movement section to Creature tab
- Added Inhabit Type section to Creature tab
- Aligned Bronze image on Death section of Creature tab
- Added Function section to Creature tab

v0.1.8
- New Loot Tab Added
- Death section added to Creature tab
- Max+1 Logic added to Creature Loot/Pickpocket/Skinning Loot Templates

v0.1.7
- Rewards section added to Quest tab
- Find Spell popout (offline list)
- Find Title popout (offline list)
- XP Reward dropdown options
- Money reward coin UI
- Footer copyright restored
- QoL Updates to Find Buttons

v0.1.6
- Quest tab cleanup
- Creature tab beginner-friendly flags
- Item preview fixes

v0.1.0
- Initial beta release
";

            DataContext = this;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
