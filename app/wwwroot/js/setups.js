// The setup pages of classic classes, Azeroth flying, the auction house bots and Immersion Life.
import { h, T, F, C, meta, post } from './core.js';
import { secondary, confirm, runProgress, alert } from './ui.js';
import { setupPage } from './setupkit.js';

// ---------------------------------------------------------------- classic classes

export function classicSetup() {
  return setupPage({
    file: 'modules/mod_classic_classes.conf', title: T('Classic classes'), subtitle: T('mod-classic-classes  ·  modules\\mod_classic_classes.conf'),
    icon: 'gamemaster', masterKey: 'ClassicClasses.Enable', masterText: T('Classic classes on'),
    build(p) {
      p.tab(T('Classes'));
      const classes = p.card(T('Which classic classes can be played'), T('Next to the classes of Conquest of Azeroth, in the way Ascension\'s Warcraft Reborn realms played them: their Reborn spells, trainers, talents and summons. The random bots are created in the same classes.'), 'gamemaster');
      p.flags(classes, 'ClassicClasses.Classes', [1, 2, 3, 4, 5, 7, 8, 9, 11],
        [T('Warrior'), T('Paladin'), T('Hunter'), T('Rogue'), T('Priest'), T('Shaman'), T('Mage'), T('Warlock'), T('Druid')], 3, ' ');
      classes.add(p.small(T('The Death Knight is left out: his spells begin at level 55, so he would skip almost the whole world.')));
      const after = p.card(T('After a change'), null, 'client');
      after.add(p.small(F('The class choice of the game shows the classes of the client patch. After a change, open Modules › Game client and choose Install: {0} builds the patch anew. The other players need the new ZIP. Bots already made keep their class; Maintenance › Reset random bots makes them anew.', meta.product)));
    }
  });
}

// ---------------------------------------------------------------- Azeroth flying

export function flyingSetup() {
  return setupPage({
    file: 'modules/mod_azeroth_flying.conf', title: T('Azeroth flying'), subtitle: T('mod-azeroth-flying  ·  modules\\mod_azeroth_flying.conf'),
    icon: 'journey', masterKey: 'AzerothFlying.Enable', masterText: T('Flying in old Azeroth on'),
    build(p) {
      p.tab(T('Old Azeroth'));
      const fly = p.card(T('Flying mounts'), T('Mounts with a flying effect may fly in Eastern Kingdoms and Kalimdor. Outland, Northrend, battlegrounds and instances keep their normal rules.'), 'journey');
      p.text(fly, 'AzerothFlying.NoFlyAreas', T('No-fly areas'), T('Area or zone ids separated by commas. Entering one of them while flying dismounts the player. Empty: no no-fly list.'));
      p.note(T('Azeroth was not built for flying: expect clipping, stuck spots, skipped quest paths, unintended shortcuts and PvP advantages. Use it at your own risk.'));
    }
  });
}

// ---------------------------------------------------------------- auction house bots

export function auctionSetup() {
  const P = 'PlayerbotsAuctions.';
  const q = s => C('quality', s);
  const Qualities = [q('Poor'), q('Common'), q('Uncommon'), q('Rare'), q('Epic'), q('Legendary')];
  const Minutes = v => v >= 120 && v % 60 === 0 ? F('{0} h', v / 60) : F('{0} min', Math.trunc(v));
  const Times = v => '×' + String(Math.round(v * 100) / 100);
  const Percent = v => Math.trunc(v) + ' %';
  const Plain = v => String(Math.trunc(v));
  const yd = v => F('{0} yd', Math.trunc(v));
  return setupPage({
    file: 'modules/mod_playerbots_auctions.conf', title: T('Auction house bots'), subtitle: T('mod-playerbots-auctions  ·  modules\\mod_playerbots_auctions.conf'),
    icon: 'auction', masterKey: P + 'Enable', masterText: T('Auction house bots on'),
    build(p) {
      p.tab(T('The bots'));
      const who = p.card(T('Who trades'), T('Every bot has a character of its own that never changes: whether it collects or tidies up, haggles or pays, crafts for profit or for skill. These settings set the frame.'), 'accounts');
      p.slider(who, P + 'MinBotLevel', T('Lowest level of trading bots'), 1, 80, 1);
      p.slider(who, P + 'Character.AvoidersPercent', T('Bots that never use the auction house'), 0, 100, 1, Percent, T('They sell everything to a vendor, as many players do.'));
      const trips = p.card(T('Trips to the auction house'), T('Like a player, a bot decides for itself when it is time to go: when its bags fill up or it has collected enough to sell. Out in the world it takes its hearthstone to a capital, walks to the auctioneers and later returns.'), 'globe');
      p.toggle(trips, P + 'CityTrip.Enable', T('Bots set out for the auction house on their own'));
      p.pair(trips, P + 'CityTrip.ItemsMin', P + 'CityTrip.ItemsMax', T('Things to sell before setting out'), 1, 30, 1, Plain, T('Tidy bots go with few, collectors with many.'));
      p.pair(trips, P + 'CityTrip.CooldownMinutesMin', P + 'CityTrip.CooldownMinutesMax', T('Time between two trips'), 10, 600, 10, Minutes);
      p.pair(trips, P + 'VisitCooldownMinutesMin', P + 'VisitCooldownMinutesMax', T('Time between visits in town'), 5, 240, 5, Minutes);
      p.slider(trips, P + 'AuctioneerRange', T('Distance to the auctioneer'), 5, 100, 1, yd);
      const pace = p.card(T('Pace'), T('How often the module looks at the bots and how many of them do their business each time; keeps the load even with thousands of bots.'), 'server');
      p.slider(pace, P + 'IntervalSeconds', T('Look at the bots every'), 5, 300, 5, v => F('{0} s', Math.trunc(v)));
      p.slider(pace, P + 'BotsPerCycle', T('Bots per look'), 1, 100, 1, Plain);
      p.toggle(pace, P + 'DailyRhythm', T('Busier in the evening and at the weekend than at night'));
      const memory = p.card(T('Memory and mail'), null, 'database');
      p.toggle(memory, P + 'SaveMemory', T('Bots remember prices over a restart'), T('What things sold for and what came back unsold is kept in two small tables of the characters database.'));
      p.toggle(memory, P + 'PauseAuctionsWhileOffline', T('Auctions pause while the server is off'), T('On a server that only runs for an evening, the auctions would otherwise be gone by the next one.'));
      p.toggle(memory, P + 'CollectAuctionMail', T('Bots empty their auction mail'));
      p.toggle(memory, P + 'Debug', T('Write what the bots do to the server log (for testing)'));

      p.tab(T('Selling'));
      const what = p.card(T('What is sold'), T('A bot only sells what it does not need itself and what is not soulbound. Items that bind when picked up and quest items are never sold.'), 'auction');
      p.choice(what, P + 'Sell.MinQuality.Equipment', T('Weapons and armor from'), ['0', '1', '2', '3', '4'], Qualities.slice(0, 5));
      p.choice(what, P + 'Sell.MinQuality.Other', T('Everything else from'), ['0', '1', '2', '3', '4'], Qualities.slice(0, 5));
      p.choice(what, P + 'Sell.MaxQuality', T('Up to'), ['2', '3', '4', '5'], Qualities.slice(2));
      const classes = p.card(T('Kinds of items'), T('What may go into the auction house. Projectiles are left out: bots are handed their ammunition.'), 'modules');
      p.flags(classes, P + 'Sell.ItemClasses', [0, 1, 2, 3, 4, 5, 7, 9, 11, 12, 13, 15, 16],
        [T('Consumables'), T('Bags'), T('Weapons'), T('Gems'), T('Armor'), T('Reagents'), T('Trade goods'), T('Recipes'), T('Quivers'), T('Quest items (tradable)'), T('Keys'), T('Pets, mounts, misc.'), T('Glyphs')]);
      const how = p.card(T('How'), null, 'settings');
      p.toggle(how, P + 'Sell.ChargeDeposit', T('Bots pay the deposit like players'));
      p.toggle(how, P + 'Sell.KeepFromVendor', T('Bots keep what is worth an auction from the vendor'));
      p.toggle(how, P + 'Sell.JunkToVendor', T('Bots sell their junk to a vendor nearby'));
      p.slider(how, P + 'Sell.MinAuctionValueSilver', T('Smallest auction worth creating'), 0, 100, 1, v => F('{0} silver', Math.trunc(v)));
      p.slider(how, P + 'Sell.MaxAuctionsPerHouse', T('Auctions per auction house at most'), 1000, 50000, 1000, Plain);
      const never = p.card(T('Never sold'), null, 'maintenance');
      p.text(never, P + 'Sell.ExcludedItemIDs', T('Item ids'), T('Separated by commas.'));
      p.text(never, P + 'Sell.ExcludedNameParts', T('Names containing'), T('Parts of names separated by commas; keeps test and technical items out.'));

      p.tab(T('Loot filter'));
      const filter = p.card(T('What bots pick up'), T('Random bots leave behind what they do not need, so their bags stay free for the auction house. Quest items, items of a quest the bot is doing and the always-loot list are never filtered. Bots of real players are left alone.'), 'maintenance');
      p.toggle(filter, P + 'LootFilter.Enable', T('Filter what random bots pick up'));
      p.choice(filter, P + 'LootFilter.MinQuality', T('Bots pick up from'), ['0', '1', '2', '3', '4', '5'], Qualities, T('Anything below this quality stays on the corpse.'));
      p.toggle(filter, P + 'LootFilter.SkipFood', T('Leave food and drink on the corpse'), T('The bot gets its own food and drink from Playerbots when it needs them.'));
      p.toggle(filter, P + 'LootFilter.SkipOtherConsumables', T('Leave potions, elixirs, flasks, scrolls and bandages'), T('Food and drink are covered by the switch above.'));
      const neverLoot = p.card(T('Never picked up'), null, 'maintenance');
      p.text(neverLoot, P + 'LootFilter.SkipItemIDs', T('Item ids'), T('Separated by commas.'));
      p.text(neverLoot, P + 'LootFilter.SkipNameParts', T('Names containing'), T('Parts of names separated by commas; upper and lower case do not matter.'));

      p.tab(T('Prices'));
      const factors = p.card(T('Starting prices'), T('The vendor value of an item times the factor of its quality. What auctions really sell for moves this usual price over time.'), 'auction');
      p.slider(factors, P + 'Price.Poor', T('Poor (grey)'), 0.5, 10, 0.5, Times);
      p.slider(factors, P + 'Price.Normal', T('Common (white)'), 0.5, 20, 0.5, Times);
      p.slider(factors, P + 'Price.Uncommon', T('Uncommon (green)'), 1, 40, 0.5, Times);
      p.slider(factors, P + 'Price.Rare', T('Rare (blue)'), 1, 60, 1, Times);
      p.slider(factors, P + 'Price.Epic', T('Epic (purple)'), 1, 100, 1, Times);
      p.slider(factors, P + 'Price.Legendary', T('Legendary (orange)'), 1, 200, 1, Times);
      const limits = p.card(T('Limits'), null, 'settings');
      p.toggle(limits, P + 'Price.LearnFromSales', T('Prices learn from what really sold'));
      p.slider(limits, P + 'Price.MinVendorFactor', T('Never below the vendor value'), 1, 5, 0.1, Times);
      p.slider(limits, P + 'Price.MaxBuyoutGold', T('Highest buyout'), 0, 20000, 100, v => v === 0 ? T('no limit') : F('{0} g', Math.trunc(v)));
      const band = p.card(T('Price band'), T('Bots ask within the low, usual and high prices an item really sold for. Until there are enough sales, a band around the usual vendor-based price is used. No external price data is needed.'), 'auction');
      p.toggle(band, P + 'Price.Band.Enable', T('Only ask within what really sold'));
      p.slider(band, P + 'Price.Band.MinSales', T('Sales before the band is its own'), 1, 20, 1, Plain);
      p.slider(band, P + 'Price.Band.FallbackLowPercent', T('Fallback low'), 10, 100, 5, Percent);
      p.slider(band, P + 'Price.Band.FallbackHighPercent', T('Fallback high'), 100, 300, 5, Percent);
      p.slider(band, P + 'Price.Band.MaxRegularPercent', T('Highest price'), 100, 1000, 25, Percent, T('Of the usual vendor-based price. A wild sale cannot lift the band above this.'));

      p.tab(T('Buying'));
      const buy = p.card(T('Buying and bidding'), T('A bot looks through some of the items on offer for things it has a use for: upgrades, things it uses up, bags, materials for its professions. Of several offers it takes the cheapest.'), 'auction');
      p.toggle(buy, P + 'Buy.Enable', T('Bots buy and bid'));
      p.toggle(buy, P + 'Buy.FromPlayers', T('From players'));
      p.toggle(buy, P + 'Buy.FromBots', T('From other bots'));
      p.toggle(buy, P + 'Buy.PlaceBids', T('Bots bid too (a bid can outbid a player)'));
      p.toggle(buy, P + 'Buy.UseBotMoney', T('Bots pay with their own money'));
      p.slider(buy, P + 'Buy.ItemsLookedAt', T('Items looked at per visit'), 10, 500, 10, Plain);
      p.slider(buy, P + 'Buy.MinVendorFactor', T('Always willing to pay'), 1, 5, 0.1, Times, T('Times the vendor value, so selling to the bots is never worse than selling to a vendor.'));
      p.slider(buy, P + 'Buy.VendorItemMaxPercent', T('For vendor goods at most'), 10, 100, 5, Percent, T('Of the vendor\'s price, so nobody can buy from a vendor and sell to the bots for a profit.'));

      p.tab(T('Crafting'));
      const craft = p.card(T('Crafting'), T('At the auctioneers a bot thinks through its recipes and compares what a product would bring with what its materials cost. What it makes and does not need, it sells.'), 'maintenance');
      p.toggle(craft, P + 'Crafting.Enable', T('Bots craft'));
      p.toggle(craft, P + 'Crafting.WalkToAnvilForgeFire', T('They walk to an anvil, forge or fire when a recipe needs one'));
      p.toggle(craft, P + 'Crafting.BuyMaterials', T('They buy missing materials in the auction house'));
      p.toggle(craft, P + 'Crafting.BuyVendorMaterials', T('They buy thread, vials and the like from a vendor'));
      p.toggle(craft, P + 'Crafting.TakeApart', T('Prospecting, milling and disenchanting'));
      p.toggle(craft, P + 'Crafting.Scrolls', T('Enchanters sell their enchantments as scrolls'));
      p.toggle(craft, P + 'Crafting.Glyphs', T('Scribes make glyphs'), T('The classes of Conquest of Azeroth cannot use the glyphs of Wrath of the Lich King.'));
      const worth = p.card(T('When it is worth it'), null, 'settings');
      p.slider(worth, P + 'Crafting.MinProfitPercent', T('Least profit'), 0, 200, 5, Percent);
      p.slider(worth, P + 'Crafting.SkillUpBonusPercent', T('A recipe that raises skill is worth more'), 0, 300, 10, v => '+' + Math.trunc(v) + ' %');
      p.slider(worth, P + 'Crafting.MaxMaterialPriceFactor', T('Most paid for a material'), 1, 5, 0.1, Times, T('Times its usual price.'));
      p.slider(worth, P + 'Crafting.RecipesPerVisit', T('Recipes thought through per visit'), 5, 200, 5, Plain);
      p.slider(worth, P + 'Crafting.MaxInARow', T('Most of one thing in one go'), 1, 20, 1, Plain);

      p.tab(T('Chat'));
      const deals = p.card(T('Deals by chat'), T('A player writes WTB or WTS into a channel - the item typed out or linked, a number before it, a price after it - and bots answer by whisper. The goods come by mail, cash on delivery.'), 'accounts');
      p.toggle(deals, P + 'Chat.Enable', T('Bots answer WTB and WTS'));
      p.slider(deals, P + 'Chat.Answers', T('Bots answering one line at most'), 1, 10, 1, Plain);
      p.text(deals, P + 'Chat.Channels', T('Channels read'), T('Parts of channel names separated by commas, for example general, trade. Empty: every channel.'));
      p.toggle(deals, P + 'Chat.Meet', T('A bot nearby offers to meet'));
      p.slider(deals, P + 'Chat.MeetDistance', T('Nearby means within'), 10, 2000, 10, yd);
      p.slider(deals, P + 'Chat.MailDelay', T('The parcel takes'), -1, 3600, 1, v => v < 0 ? T('like mail') : v < 120 ? F('{0} s', Math.trunc(v)) : F('{0} min', Math.trunc(v / 60)), T('"Like mail" follows Mail.DeliveryDelay of worldserver.conf.'));
      const chatter = p.card(T('Chatter'), T('Every bot is somebody in chat too: 65 personalities with more than 16000 lines between them.'), 'accounts');
      p.toggle(chatter, P + 'Chatter.Enable', T('The bots talk'));
      p.slider(chatter, P + 'Chatter.Interval', T('Something said out of the blue about every'), 20, 3600, 10, v => v < 120 ? F('{0} s', Math.trunc(v)) : F('{0} min', String(Math.round(v / 6) / 10)), T('Lower is livelier.'));
      p.toggle(chatter, P + 'Chatter.Replies', T('They answer a hello, a ding, a thanks, a joke request'));
      p.toggle(chatter, P + 'Chatter.Nearby', T('A bot within earshot says something now and then'));
      p.toggle(chatter, P + 'Chatter.WorldChannel', T('Use the realm\'s channel when no bot is in the player\'s zone'));
      p.slider(chatter, P + 'Chatter.Mixed', T('Bots that mix two personalities'), 0, 100, 5, Percent);
      p.slider(chatter, P + 'Chatter.EasterEggs', T('A wink at AFK Realm once in'), 0, 2000, 10, v => v === 0 ? T('never') : F('{0} lines', Math.trunc(v)));

      p.tab(T('Trading Post'));
      const tp = p.card(T('The Trading Post'), T('A last resort at every auctioneer for what bots hardly come by: materials and what crafters make for everybody, well above the usual price. Only players can use it; the money is gone from the world.'), 'auction');
      p.toggle(tp, P + 'TradingPost.Enable', T('Auctioneers offer the Trading Post'));
      p.toggle(tp, P + 'TradingPost.Flyer', T('Players get its flyer once a day'));
      p.toggle(tp, P + 'TradingPost.FreeSample', T('A free sample once a week (always junk)'));
      p.choice(tp, P + 'TradingPost.MaxQuality', T('Best quality sold'), ['1', '2', '3', '4'], Qualities.slice(1, 5));
      p.slider(tp, P + 'TradingPost.ScarceBelow', T('Sells a material while the house has fewer than'), 1, 200, 1, v => F('{0} pcs', Math.trunc(v)));
      const tpPrice = p.card(T('Its prices and stock'), T('Prices as multiples of what a thing usually goes for: the buyout is delivered at once, a bid when the auction ends.'), 'settings');
      p.slider(tpPrice, P + 'TradingPost.Price.Materials', T('Buyout of materials'), 1, 10, 0.1, Times);
      p.slider(tpPrice, P + 'TradingPost.Price.Crafted', T('Buyout of crafted goods'), 1, 10, 0.1, Times);
      p.slider(tpPrice, P + 'TradingPost.Bid.Materials', T('Lowest bid for materials'), 1, 10, 0.1, Times);
      p.slider(tpPrice, P + 'TradingPost.Bid.Crafted', T('Lowest bid for crafted goods'), 1, 10, 0.1, Times);
      p.slider(tpPrice, P + 'TradingPost.Stock.Materials', T('Pieces of a material per day'), 1, 500, 1, Plain);
      p.slider(tpPrice, P + 'TradingPost.Stock.Crafted', T('Pieces of a crafted thing per day'), 1, 200, 1, Plain);
      p.slider(tpPrice, P + 'TradingPost.Daily.Materials', T('Most a character buys a day (materials)'), 1, 200, 1, Plain);
      p.slider(tpPrice, P + 'TradingPost.Daily.Crafted', T('Most a character buys a day (crafted)'), 1, 100, 1, Plain);
      const tpNever = p.card(T('Never at the Trading Post'), null, 'maintenance');
      p.text(tpNever, P + 'TradingPost.ExcludedItemIDs', T('Item ids'), T('What is meant to be scarce: materials made on a cooldown and the like.'));
    }
  });
}

// ---------------------------------------------------------------- Immersion Life

const Seconds = v => v >= 60 ? F('{0} min', Math.trunc(v / 60)) : F('{0} s', Math.trunc(v));
const Pct = v => Math.trunc(v) + ' %';
const Yards = v => F('{0} yd', Math.trunc(v));

function placeName(key) {
  const questing = key.endsWith('Questing');
  if (questing) key = key.slice(0, -'Questing'.length);
  const name = key.replace(/(?<=[a-z])(?=[A-Z])/g, ' ');
  const shown = T(name);
  return questing ? shown + ' – ' + T('Questing') : shown;
}

export function immersionSetup() {
  return setupPage({
    file: 'modules/mod_playerbots_immersion.conf', title: T('Immersion Life'), subtitle: T('mod-playerbots-immersion  -  modules\\mod_playerbots_immersion.conf'),
    icon: 'accounts', masterKey: 'CityLife.Enable', masterText: T('Immersion Life on'),
    build(p) {
      const keys = p.conf.keys;
      const groups = (prefix, filter) => {
        const out = new Map();
        for (const k of keys) {
          if (!k.toLowerCase().startsWith(prefix.toLowerCase()) || (filter && !filter(k))) continue;
          const g = k.slice(prefix.length).split('.')[0];
          if (!out.has(g)) out.set(g, []);
          out.get(g).push(k);
        }
        return out;
      };

      p.tab(T('City population'));
      const population = p.card(T('City population'), T('These bots are borrowed from the normal playerbot pool and reserved while they stay. The limit is shared by city residents, questers, legendary characters and fishers; PvP has its own budget.'), 'accounts');
      p.slider(population, 'CityLife.Population.MaxTotal', T('At most at once'), 20, 1000, 10, v => String(Math.trunc(v)));
      p.toggle(population, 'CityLife.Time.Enable', T('More people in the evening'), T('The population follows the time of day: fewer in the morning and at night, full in the evening.'));
      const day = p.card(T('Time of day'), null, 'sun');
      p.slider(day, 'CityLife.Time.MorningMultiplierPct', T('Morning'), 0, 200, 5, Pct);
      p.slider(day, 'CityLife.Time.DayMultiplierPct', T('Day'), 0, 200, 5, Pct);
      p.slider(day, 'CityLife.Time.EveningMultiplierPct', T('Evening'), 0, 200, 5, Pct);
      p.slider(day, 'CityLife.Time.NightMultiplierPct', T('Night'), 0, 200, 5, Pct);
      const life = p.card(T('Movement and life'), null, 'globe');
      p.toggle(life, 'CityLife.Behaviour.Relocate', T('Residents move between places'));
      p.pair(life, 'CityLife.Behaviour.RelocateMinSeconds', 'CityLife.Behaviour.RelocateMaxSeconds', T('Time between two moves'), 30, 3600, 30, Seconds);
      p.slider(life, 'CityLife.Behaviour.StaticServiceChancePct', T('Standing at auction house and bank'), 0, 100, 5, Pct);
      p.slider(life, 'CityLife.Behaviour.ServiceSpotWeightMultiplierPct', T('Pull toward service spots'), 0, 500, 10, Pct);
      p.slider(life, 'CityLife.Behaviour.LeashRadius', T('Distance from their place'), 10, 100, 5, Yards);
      p.toggle(life, 'CityLife.Behaviour.Emotes', T('Emotes'));
      p.slider(life, 'CityLife.Behaviour.EmoteChancePct', T('Chance for an emote'), 0, 100, 5, Pct);
      p.pair(life, 'CityLife.Behaviour.EmoteMinSeconds', 'CityLife.Behaviour.EmoteMaxSeconds', T('Time between two emotes'), 30, 3600, 30, Seconds);
      p.slider(life, 'CityLife.Behaviour.WalkChancePct', T('Residents that walk instead of running'), 0, 100, 5, Pct);
      const chatter = p.card(T('Chatter'), null, 'accounts');
      p.slider(chatter, 'CityLife.Custom.Chat.Range', T('Chat range'), 5, 100, 5, Yards);
      p.toggle(chatter, 'CityLife.Custom.Conversations.Enable', T('Conversations between residents'));
      p.pair(chatter, 'CityLife.Custom.Conversations.MinSeconds', 'CityLife.Custom.Conversations.MaxSeconds', T('Time between two conversations'), 30, 3600, 30, Seconds);
      p.slider(chatter, 'CityLife.Custom.Conversations.MaxConcurrent', T('At most at once'), 1, 10, 1);
      p.slider(chatter, 'CityLife.Custom.Conversations.Range', T('Conversation range'), 1, 40, 1, Yards);
      p.slider(chatter, 'CityLife.Custom.Conversations.PartnerRange', T('Distance between speakers'), 1, 25, 1, Yards);
      p.pair(chatter, 'CityLife.Custom.Conversations.ReplyMinSeconds', 'CityLife.Custom.Conversations.ReplyMaxSeconds', T('Reply after'), 1, 30, 1, Seconds);
      p.toggle(chatter, 'CityLife.Custom.PlayerReactions.Enable', T('React to players'));
      p.slider(chatter, 'CityLife.Custom.PlayerReactions.ChancePct', T('Chance for a reaction'), 0, 100, 5, Pct);
      p.slider(chatter, 'CityLife.Custom.PlayerReactions.CooldownSeconds', T('Reaction cooldown'), 0, 600, 5, Seconds);
      p.slider(chatter, 'CityLife.Custom.PlayerReactions.Range', T('Reaction range'), 1, 40, 1, Yards);
      for (const [prefix, title] of [['CityLife.Custom.Legendary', T('Legendary residents')], ['CityLife.Custom.Beggar', T('Beggars')], ['CityLife.Custom.Buyer', T('Buyers')]]) {
        const card = p.card(title, null, 'accounts');
        p.pair(card, prefix + '.ChatMinSeconds', prefix + '.ChatMaxSeconds', T('Time between two lines'), 30, 3600, 30, Seconds);
        p.slider(card, prefix + '.YellChancePct', T('Yell chance'), 0, 100, 5, Pct);
      }

      p.tab(T('Cities and towns'));
      for (const [g] of groups('CityLife.Hub.')) {
        const root = 'CityLife.Hub.' + g + '.';
        if (keys.some(k => k.toLowerCase() === (root + 'Questing').toLowerCase())) continue;
        const card = p.card(placeName(g), null, 'globe');
        p.toggle(card, root + 'Enable', T('On'));
        p.slider(card, root + 'Population', T('Population'), 0, 100, 1);
      }

      p.tab(T('Custom city characters'));
      const custom = p.card(T('Custom city characters'), null, 'accounts');
      p.toggle(custom, 'CityLife.Custom.Enable', T('Custom city characters on'));
      p.toggle(custom, 'CityLife.Custom.CreateMissing.Enable', T('Create missing bots'));
      p.slider(custom, 'CityLife.Custom.CreateMissing.MaxBots', T('At most at once'), 0, 100, 1);
      p.toggle(custom, 'CityLife.Custom.Legendary.Enable', T('Legendary residents'));
      p.toggle(custom, 'CityLife.Custom.Legendary.FixedIdentity', T('Fixed names and outfits'));
      p.toggle(custom, 'CityLife.Custom.Beggar.Enable', T('Beggars'));
      p.toggle(custom, 'CityLife.Custom.Beggar.Wander.Walk', T('Beggars walk instead of running'));
      p.toggle(custom, 'CityLife.Custom.Buyer.Enable', T('Buyers'));
      p.toggle(custom, 'CityLife.Custom.TrainingDummy.Enable', T('Training scenes'));
      for (const role of ['Legendary', 'Beggar', 'Buyer', 'TrainingDummy']) {
        const pre = 'CityLife.Custom.' + role + '.';
        const list = keys.filter(k => k.toLowerCase().startsWith(pre.toLowerCase()) && k.toLowerCase().endsWith('.population'));
        if (!list.length) continue;
        const title = role === 'Legendary' ? T('Legendary residents') : role === 'Beggar' ? T('Beggars') : role === 'Buyer' ? T('Buyers') : T('Training scenes');
        const card = p.card(title, null, 'accounts');
        for (const k of list) p.slider(card, k, placeName(k.slice(pre.length).split('.')[0]), 0, 100, 1);
      }

      p.tab(T('Fishing'));
      const fishing = p.card(T('Fishing Life'), T('Fishing Life places fishers at the waterfronts. It is off by default and counts inside the city limit.'), 'journey');
      p.toggle(fishing, 'FishingLife.Enable', T('Fishing Life on'));
      p.toggle(fishing, 'FishingLife.CreateMissing.Enable', T('Create missing bots'));
      p.slider(fishing, 'FishingLife.CreateMissing.MaxBots', T('Fishers at most'), 0, 120, 1);
      for (const [g] of groups('FishingLife.', k => k.toLowerCase().endsWith('.population'))) {
        const card = p.card(placeName(g), null, 'journey');
        const root = 'FishingLife.' + g + '.';
        p.toggle(card, root + 'Enable', T('On'));
        p.slider(card, root + 'Population', T('Population'), 0, 50, 1);
      }
      const fchat = p.card(T('Fishing chatter'), null, 'journey');
      p.slider(fchat, 'FishingLife.Chat.Range', T('Chat range'), 5, 100, 5, Yards);
      p.pair(fchat, 'FishingLife.ChatMinSeconds', 'FishingLife.ChatMaxSeconds', T('Time between two lines'), 30, 3600, 30, Seconds);
      p.slider(fchat, 'FishingLife.YellChancePct', T('Yell chance'), 0, 100, 5, Pct);

      p.tab(T('PvP'));
      const pvp = p.card(T('PvP Life'), T('PvP Life brings duels, world PvP and faction campaigns. It is off by default for Conquest of Azeroth and uses its own budget.'), 'gamemaster');
      p.toggle(pvp, 'PvPLife.Enable', T('PvP Life on'));
      p.slider(pvp, 'PvPLife.Bots.MaxPerSide', T('Bots per side'), 0, 200, 5);
      p.toggle(pvp, 'PvPLife.Duel.Enable', T('Duel zones'));
      p.toggle(pvp, 'PvPLife.World.AlwaysActive', T('World PvP'));
      p.toggle(pvp, 'PvPLife.ForTheHorde.Enable', T('Horde'));
      p.toggle(pvp, 'PvPLife.ForTheAlliance.Enable', T('Alliance'));
      for (const [g] of groups('PvPLife.Zone.')) {
        const card = p.card(g.replace(/_/g, ' '), null, 'gamemaster');
        const root = 'PvPLife.Zone.' + g + '.';
        p.toggle(card, root + 'Enable', T('On'));
        p.pair(card, root + 'MinPopulation', root + 'MaxPopulation', T('Population'), 0, 100, 1);
      }
      const pchat = p.card(T('PvP chatter'), null, 'gamemaster');
      p.toggle(pchat, 'PvPLife.BotChat.Enable', T('Chatter'));
      p.slider(pchat, 'PvPLife.BotChat.WorldChance', T('World chat chance'), 0, 100, 5, Pct);
      p.slider(pchat, 'PvPLife.BotChat.YellChance', T('Yell chance'), 0, 100, 5, Pct);

      p.tab(T('Database tables'));
      const database = p.card(T('Database tables'), T('The module keeps its cities, custom characters, fishing spots and PvP locations in the world and characters databases. Install these tables once before the first start.'), 'database');
      const status = p.small(T('Only needed once. Existing tables and rows are kept.'));
      database.add(secondary(T('Install the database tables ...'), async () => {
        if (!await confirm(T('Install the database tables now? This creates the module\'s tables in the world and characters databases. It is only needed once; existing tables and rows are kept.'))) return;
        const j = await runProgress(() => post('/api/setups/immersion/database'), T('Installing the database tables ...'), { quiet: true });
        if (!j) return;
        status.textContent = j.ok ? j.message : T('The database tables could not be installed:') + ' ' + j.message;
        if (!j.ok) alert(T('The database tables could not be installed:') + '\n\n' + j.message);
      }), status);
      p.note(T('Stop the worldserver before installing the tables. The changes show after the next start.'));
    }
  });
}
