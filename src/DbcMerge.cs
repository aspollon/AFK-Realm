using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// Puts a CoA Custom server table on top of the game client's own table instead of copying it over CoA's.
    ///
    /// The server must agree with the client, and copying the package's files whole broke tables the package carries
    /// in an older state than CoA (AppearanceCategories: Bloodmage and Venomancer lost their Incarnations;
    /// SkillRaceClassInfo and CharacterCreationShapeshiftDetails lost CoA rows; Faction: swapped reputations). So the
    /// client's table is the base, and only rows the client does not have come from the package. Rows of a table the
    /// core reads are keyed by their id; their text fields are moved into the merged string block by the core's own
    /// table format (DBCfmt.h), so a package row keeps its names. A table the client lacks is taken from the package.
    /// </summary>
    static class DbcMerge
    {
        public sealed class Result
        {
            public byte[] Bytes;
            public string How;      // short note for the log
            public int Added;       // package rows added on top of the client's
        }

        /// <summary>Byte offsets of the text fields in each table the core loads (from DBCfmt.h; 's' fields).</summary>
        static readonly Dictionary<string, int[]> CoreTexts = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Achievement.dbc", new int[] { 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72, 76 } },
            { "Achievement_Category.dbc", new int[0] },
            { "Achievement_Criteria.dbc", new int[0] },
            { "AreaGroup.dbc", new int[0] },
            { "AreaPOI.dbc", new int[0] },
            { "AreaTable.dbc", new int[] { 44, 48, 52, 56, 60, 64, 68, 72, 76, 80, 84, 88, 92, 96, 100, 104 } },
            { "AuctionHouse.dbc", new int[0] },
            { "BankBagSlotPrices.dbc", new int[0] },
            { "BarberShopStyle.dbc", new int[0] },
            { "BattlemasterList.dbc", new int[] { 44, 48, 52, 56, 60, 64, 68, 72, 76, 80, 84, 88, 92, 96, 100, 104 } },
            { "CharSections.dbc", new int[0] },
            { "CharStartOutfit.dbc", new int[0] },
            { "CharTitles.dbc", new int[] { 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 76, 80, 84, 88, 92, 96, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136 } },
            { "ChatChannels.dbc", new int[] { 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72 } },
            { "ChrClasses.dbc", new int[] { 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72, 76 } },
            { "ChrRaces.dbc", new int[] { 56, 60, 64, 68, 72, 76, 80, 84, 88, 92, 96, 100, 104, 108, 112, 116 } },
            { "CinematicCamera.dbc", new int[] { 4 } },
            { "CinematicSequences.dbc", new int[0] },
            { "CreatureDisplayInfo.dbc", new int[0] },
            { "CreatureDisplayInfoExtra.dbc", new int[0] },
            { "CreatureFamily.dbc", new int[] { 40, 44, 48, 52, 56, 60, 64, 68, 72, 76, 80, 84, 88, 92, 96, 100 } },
            { "CreatureModelData.dbc", new int[0] },
            { "CreatureSpellData.dbc", new int[0] },
            { "CreatureType.dbc", new int[0] },
            { "CurrencyTypes.dbc", new int[0] },
            { "DestructibleModelData.dbc", new int[0] },
            { "DungeonEncounter.dbc", new int[] { 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72, 76, 80 } },
            { "DurabilityCosts.dbc", new int[0] },
            { "DurabilityQuality.dbc", new int[0] },
            { "Emotes.dbc", new int[0] },
            { "EmotesText.dbc", new int[0] },
            { "EmotesTextSound.dbc", new int[0] },
            { "Faction.dbc", new int[] { 92, 96, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 144, 148, 152 } },
            { "FactionTemplate.dbc", new int[0] },
            { "GameObjectArtKit.dbc", new int[0] },
            { "GameObjectDisplayInfo.dbc", new int[] { 4 } },
            { "GemProperties.dbc", new int[0] },
            { "GlyphProperties.dbc", new int[0] },
            { "GlyphSlot.dbc", new int[0] },
            { "Holidays.dbc", new int[] { 204 } },
            { "Item.dbc", new int[0] },
            { "ItemBagFamily.dbc", new int[0] },
            { "ItemCondExtCosts.dbc", new int[0] },
            { "ItemDisplayInfo.dbc", new int[] { 20 } },
            { "ItemExtendedCost.dbc", new int[0] },
            { "ItemLimitCategory.dbc", new int[0] },
            { "ItemRandomProperties.dbc", new int[] { 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72, 76, 80, 84, 88 } },
            { "ItemRandomSuffix.dbc", new int[] { 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64 } },
            { "ItemSet.dbc", new int[] { 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64 } },
            { "LFGDungeons.dbc", new int[] { 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64 } },
            { "Light.dbc", new int[0] },
            { "LiquidType.dbc", new int[0] },
            { "Lock.dbc", new int[0] },
            { "MailTemplate.dbc", new int[] { 72, 76, 80, 84, 88, 92, 96, 100, 104, 108, 112, 116, 120, 124, 128, 132 } },
            { "Map.dbc", new int[] { 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72, 76, 80 } },
            { "MapDifficulty.dbc", new int[] { 12 } },
            { "Movie.dbc", new int[0] },
            { "NamesProfanity.dbc", new int[] { 4 } },
            { "NamesReserved.dbc", new int[] { 4 } },
            { "OverrideSpellData.dbc", new int[0] },
            { "PowerDisplay.dbc", new int[0] },
            { "PvpDifficulty.dbc", new int[0] },
            { "QuestFactionReward.dbc", new int[0] },
            { "QuestSort.dbc", new int[0] },
            { "QuestXP.dbc", new int[0] },
            { "RandPropPoints.dbc", new int[0] },
            { "ScalingStatDistribution.dbc", new int[0] },
            { "ScalingStatValues.dbc", new int[0] },
            { "SkillLine.dbc", new int[] { 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72 } },
            { "SkillLineAbility.dbc", new int[0] },
            { "SkillRaceClassInfo.dbc", new int[0] },
            { "SkillTiers.dbc", new int[0] },
            { "SoundEntries.dbc", new int[0] },
            { "Spell.dbc", new int[] { 544, 548, 552, 556, 560, 564, 568, 572, 576, 580, 584, 588, 592, 596, 600, 604, 612, 616, 620, 624, 628, 632, 636, 640, 644, 648, 652, 656, 660, 664, 668, 672 } },
            { "SpellAffect.dbc", new int[0] },
            { "SpellCastTimes.dbc", new int[0] },
            { "SpellCategory.dbc", new int[0] },
            { "SpellDifficulty.dbc", new int[0] },
            { "SpellDuration.dbc", new int[0] },
            { "SpellFocusObject.dbc", new int[0] },
            { "SpellItemEnchantment.dbc", new int[] { 56, 60, 64, 68, 72, 76, 80, 84, 88, 92, 96, 100, 104, 108, 112, 116 } },
            { "SpellItemEnchantmentCondition.dbc", new int[0] },
            { "SpellRadius.dbc", new int[0] },
            { "SpellRange.dbc", new int[0] },
            { "SpellRuneCost.dbc", new int[0] },
            { "SpellShapeshiftForm.dbc", new int[0] },
            { "SpellVisual.dbc", new int[0] },
            { "StableSlotPrices.dbc", new int[0] },
            { "SummonProperties.dbc", new int[0] },
            { "Talent.dbc", new int[0] },
            { "TalentTab.dbc", new int[0] },
            { "TaxiNodes.dbc", new int[] { 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72, 76, 80 } },
            { "TaxiPath.dbc", new int[0] },
            { "TaxiPathNode.dbc", new int[0] },
            { "TeamContributionPoints.dbc", new int[0] },
            { "TotemCategory.dbc", new int[0] },
            { "TransportAnimation.dbc", new int[0] },
            { "TransportRotation.dbc", new int[0] },
            { "Vehicle.dbc", new int[] { 116, 120, 124, 128 } },
            { "VehicleSeat.dbc", new int[0] },
            { "WMOAreaTable.dbc", new int[0] },
            { "WorldMapArea.dbc", new int[0] },
            { "WorldMapOverlay.dbc", new int[0] },
            { "gtBarberShopCostBase.dbc", new int[0] },
            { "gtChanceToMeleeCrit.dbc", new int[0] },
            { "gtChanceToMeleeCritBase.dbc", new int[0] },
            { "gtChanceToSpellCrit.dbc", new int[0] },
            { "gtChanceToSpellCritBase.dbc", new int[0] },
            { "gtCombatRatings.dbc", new int[0] },
            { "gtNPCManaCostScaler.dbc", new int[0] },
            { "gtOCTClassCombatRatingScalar.dbc", new int[0] },
            { "gtOCTRegenHP.dbc", new int[0] },
            { "gtOCTRegenMP.dbc", new int[0] },
            { "gtRegenHPPerSpt.dbc", new int[0] },
            { "gtRegenMPPerSpt.dbc", new int[0] },
        };

        public static bool CoreLoads(string file) { return CoreTexts.ContainsKey(file); }

        /// <summary>The table the server should use: client is the client's bytes (null: the client has no such table),
        /// package the package's.</summary>
        public static Result Merge(string file, byte[] client, byte[] package)
        {
            if (client == null) return new Result { Bytes = package, How = L.T("from the package (the client has no such table)") };
            DbcTable c, p;
            try { c = DbcTable.Parse(client, file); }
            catch { return new Result { Bytes = package, How = L.T("from the package (the client's table is unreadable)") }; }
            try { p = DbcTable.Parse(package, file); }
            catch { return new Result { Bytes = client, How = L.T("the client's (the package's table is unreadable)") }; }
            if (c.Fields != p.Fields || c.RecordSize != p.RecordSize)
                return new Result { Bytes = client, How = L.T("the client's (the package's table has another layout)") };

            bool byId = UniqueIds(c) && UniqueIds(p);
            var have = new HashSet<string>(c.Records.Select(r => Key(r, byId)));
            var extra = p.Records.Where(r => !have.Contains(Key(r, byId))).ToList();
            if (extra.Count == 0) return new Result { Bytes = client, How = L.T("the client's (it has every row of the package)") };

            int[] texts;
            if (!CoreTexts.TryGetValue(file, out texts))
                return new Result { Bytes = client, How = L.F("the client's ({0} package row(s) the client does not know are left out)", extra.Count) };

            // the package's text fields of its own rows go into the client's string block
            byte[] pStrings = p.Strings;
            foreach (var row in extra)
            {
                var copy = (byte[])row.Clone();
                foreach (int at in texts)
                {
                    if (at + 4 > copy.Length) continue;
                    uint offset = BitConverter.ToUInt32(copy, at);
                    uint moved = offset == 0 ? 0 : c.AddString(ReadText(pStrings, offset));
                    Buffer.BlockCopy(BitConverter.GetBytes(moved), 0, copy, at, 4);
                }
                c.Records.Add(copy);
            }
            if (byId) c.Records.Sort((a, b) => BitConverter.ToUInt32(a, 0).CompareTo(BitConverter.ToUInt32(b, 0)));
            return new Result { Bytes = c.ToBytes(), Added = extra.Count, How = L.F("the client's, with {0} row(s) of the package added", extra.Count) };
        }

        static bool UniqueIds(DbcTable t)
        {
            if (t.RecordSize < 4 || t.Fields == 0 || t.RecordSize / t.Fields != 4) return false;
            var seen = new HashSet<uint>();
            foreach (var r in t.Records) if (!seen.Add(BitConverter.ToUInt32(r, 0))) return false;
            return true;
        }

        static string Key(byte[] record, bool byId)
        {
            return byId ? BitConverter.ToUInt32(record, 0).ToString() : Convert.ToBase64String(record);
        }

        static string ReadText(byte[] block, uint offset)
        {
            if (offset >= block.Length) return "";
            int end = (int)offset;
            while (end < block.Length && block[end] != 0) end++;
            return Encoding.UTF8.GetString(block, (int)offset, end - (int)offset);
        }
    }
}
