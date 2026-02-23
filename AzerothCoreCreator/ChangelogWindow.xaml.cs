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
@"v0.1.8
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
