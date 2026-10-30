using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// The base-race page of the CoA Custom UI. It deliberately has no race switches and no importer:
    /// the CoA client decides which races are visible, and this page only says which stock race each
    /// custom race starts as, which also decides its faction, start zone and racial abilities.
    ///
    /// Only races added by CoA Custom are listed. The stock races of the base client are not shown:
    /// they need no configuration here.
    /// </summary>
    class BaseRacesSetup : SetupDialog
    {
        static readonly string[] Bases =
        {
            "Human", "Orc", "Dwarf", "NightElf", "Undead", "Tauren", "Gnome", "Troll", "BloodElf", "Draenei"
        };

        public BaseRacesSetup(Install i) : base(i, "mod_custom_races.conf",
            L.T("Base races"), L.T("mod-custom-races  ·  modules\\mod_custom_races.conf"),
            "accounts", "CustomRaces.Enable", L.T("Custom races on"))
        {
            // The CoA UI owns the race choice; a switch here would be a second, competing one.
            master.Visible = false;

            Tab(L.T("Base races"));
            var card = Card(L.T("Which stock race each CoA race starts as"),
                L.T("A custom race inherits the start zone, outfit, faction, reputations and racial abilities of its base race. " +
                    "Changing this changes the race's side and faction; it does not rebuild the core. " +
                    "Visibility is handled in the CoA UI's race menu, not here. Stock races of the base client are not listed."), "accounts");

            var rows = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Margin = new Padding(0, 4, 0, 0),
                BackColor = Ui.Surface
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

            var namesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keys = new List<string>();
            foreach (string key in RaceKeys())
            {
                int id;
                if (!int.TryParse(Raw("CustomRaces." + key + ".Id", "0"), out id) || id <= 11) continue;
                keys.Add(key);
                namesByKey[key] = CleanName(Raw("CustomRaces." + key + ".Name", key));
            }
            var duplicateNames = new HashSet<string>(keys.GroupBy(k => namesByKey[k], StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            keys.Sort((a, b) => string.Compare(namesByKey[a], namesByKey[b], StringComparison.CurrentCultureIgnoreCase));
            foreach (string key in keys)
            {
                string name = namesByKey[key];
                string current = Raw("CustomRaces." + key + ".Base", "Human");
                string shown = name;
                if (duplicateNames.Contains(name))
                    shown += " (" + L.T(HordeBase(current) ? "Horde" : "Alliance") + ")";
                int row = rows.RowCount++;
                rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var label = new Label
                {
                    Text = shown,
                    Font = Ui.Base,
                    ForeColor = Ui.Text,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 7, 8, 3),
                    BackColor = Ui.Surface
                };
                var pick = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Font = Ui.Base,
                    Width = 170,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(0, 4, 0, 4)
                };
                foreach (string b in Bases) pick.Items.Add(b);
                int at = Array.FindIndex(Bases, b => b.Equals(current, StringComparison.OrdinalIgnoreCase));
                pick.SelectedIndex = at < 0 ? 0 : at;
                pick.SelectedIndexChanged += delegate
                {
                    if (Loading) return;
                    Set("CustomRaces." + key + ".Base", "\"" + Bases[pick.SelectedIndex] + "\"");
                };

                rows.Controls.Add(label, 0, row);
                rows.Controls.Add(pick, 1, row);
            }

            card.Add(rows);
            Start();
        }

        static string CleanName(string name)
        {
            return (name ?? "")
                .Replace("_A", "")
                .Replace("_Horde", "")
                .Replace("Horde", "")
                .Replace("Alliance", "")
                .Trim();
        }

        static bool HordeBase(string baseRace)
        {
            return string.Equals(baseRace, "Orc", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(baseRace, "Undead", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(baseRace, "Tauren", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(baseRace, "Troll", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(baseRace, "BloodElf", StringComparison.OrdinalIgnoreCase);
        }

        IEnumerable<string> RaceKeys()
        {
            string list = Raw("CustomRaces.Races", "");
            foreach (string key in list.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                yield return key;
        }
    }
}
