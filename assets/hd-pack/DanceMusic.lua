--[[
Music While You Dancing, as AFK Realm installs it for CoA Custom.
Plays Music\<race><gender>.mp3 when you /dance. The song is chosen when you dance (not when the add-on loads), and
races without their own song (Vulpera, Highmountain, Nightborne ...) use the song of the race they come from.
]]--

DanceMusicEnabled = true;

local DanceMusicDebug = false;
local DanceMusicPlaying = false;
local MUSIC = "Interface\\AddOns\\DanceMusic\\Music\\";

local genders = { "Male", "Male", "Female" };
local ownSongs = { Human = true, Dwarf = true, NightElf = true, Gnome = true, Draenei = true,
                   Orc = true, Undead = true, Tauren = true, Troll = true, BloodElf = true };

-- Races that only share part of a stock race's name.
local donors = {
  Scourge = "Undead", Forsaken = "Undead", Nightborne = "NightElf", Kaldorei = "NightElf",
  VoidElf = "BloodElf", HighElf = "BloodElf", Dracthyr = "BloodElf", Sylvari = "NightElf",
  KulTiran = "Human", Worgen = "Human", Gilnean = "Human", Pandaren = "Human",
  Lightforged = "Draenei", Eredar = "Draenei", Broken = "Draenei",
  Mechagnome = "Gnome", Goblin = "Gnome", Vulpera = "Gnome",
  Maghar = "Orc", Ogre = "Orc", Earthen = "Dwarf", DarkIron = "Dwarf",
  Highmountain = "Tauren", Zandalari = "Troll",
};

local function Debug(msg)
  if DanceMusicDebug then
    DEFAULT_CHAT_FRAME:AddMessage("DanceMusic: " .. msg);
  end
end

local function SongRace()
  local _, race = UnitRace("player");
  race = race or "";
  if ownSongs[race] then return race end
  if donors[race] then return donors[race] end
  for name, stock in pairs(donors) do
    if string.find(race, name, 1, true) then return stock end
  end
  for stock in pairs(ownSongs) do
    if string.find(race, stock, 1, true) then return stock end
  end
  return UnitFactionGroup("player") == "Horde" and "Orc" or "Human";
end

local function SongFile()
  return MUSIC .. SongRace() .. genders[UnitSex("player") or 2] .. ".mp3";
end

function DanceMusic_Debug(msg) Debug(msg) end

function DanceMusic_SlashCommandHandler(command)
  if command == "on" then
    DanceMusicEnabled = true;
    DEFAULT_CHAT_FRAME:AddMessage(DANCEMUSIC_ENABLED);
  elseif command == "off" then
    DanceMusicEnabled = false;
    DEFAULT_CHAT_FRAME:AddMessage(DANCEMUSIC_DISABLED);
  elseif command == "debug" then
    DanceMusicDebug = not DanceMusicDebug;
    DEFAULT_CHAT_FRAME:AddMessage(DanceMusicDebug and DANCEMUSIC_DEBUG_ENABLED or DANCEMUSIC_DEBUG_DISABLED);
  elseif command == "test" then
    local file = SongFile();
    DEFAULT_CHAT_FRAME:AddMessage("DanceMusic: playing " .. file);
    if GetCVar("Sound_EnableMusic") == "0" then
      DEFAULT_CHAT_FRAME:AddMessage("DanceMusic: game music is switched off (Sound options), so the song cannot be heard.");
    end
    PlayMusic(file);
    DanceMusicPlaying = true;
  else
    DEFAULT_CHAT_FRAME:AddMessage(DanceMusicEnabled and DANCEMUSIC_ENABLED or DANCEMUSIC_DISABLED);
    DEFAULT_CHAT_FRAME:AddMessage(DANCEMUSIC_HELP);
  end
end

function DanceMusic_DoEmote(emote, msg)
  if DanceMusicEnabled and emote == "DANCE" and not DanceMusicPlaying then
    local file = SongFile();
    Debug("start " .. file);
    PlayMusic(file);
    DanceMusicPlaying = true;
  end
end

function DanceMusic_StopMusic()
  if DanceMusicPlaying then
    StopMusic();
    DanceMusicPlaying = false;
    Debug("stopped");
  end
end

function DanceMusic_OnLoad()
  SlashCmdList["DanceMusic"] = DanceMusic_SlashCommandHandler;
  SLASH_DanceMusic1 = "/dancemusic";
  hooksecurefunc("DoEmote", DanceMusic_DoEmote);
  -- Dancing stops when the character moves.
  for _, name in ipairs({ "MoveForwardStart", "MoveBackwardStart", "StrafeLeftStart", "StrafeRightStart",
                          "JumpOrAscendStart", "ToggleAutoRun" }) do
    if _G[name] then hooksecurefunc(name, DanceMusic_StopMusic) end
  end
end
