const host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
const $ = id => document.getElementById(id);
const demo = !host;
const demoReady = demo && new URLSearchParams(location.search).has("ready");
let state = {};
let accountMode = "register";
let accountPending = false;
let accountTimer = 0;

const accountModes = {
  register: {
    title: "Create your game account",
    intro: "You need one account for the game and the launcher. Pick a name and password, then use the same ones to log in to the game.",
    submit: "Create account",
    note: "Use this name and password when the game asks you to log in."
  },
  login: {
    title: "Log in to your game account",
    intro: "Use the account name and password you already use on this server.",
    submit: "Log in",
    note: "The launcher remembers the name. You still use it with your password in the game."
  }
};

function send(action, data = {}) {
  if (host) host.postMessage({ action, ...data });
}

function render(next) {
  state = next || {};
  document.body.classList.toggle("is-busy", !!state.busy);
  $("status").textContent = state.status || "";
  $("status").className = "status " + (state.statusKind || "");
  $("detail").textContent = state.detail || "";
  $("progress").style.width = Math.max(0, Math.min(100, state.progress || 0)) + "%";
  const folder = state.folder || "";
  $("folder").textContent = folder ? shortened(folder) : "Not selected";
  $("folder").title = folder;
  $("versionLabel").textContent = state.version ? "Manifest " + state.version : "Manifest not checked";
  $("portalLabel").textContent = state.portalUrl || "";
  const needsSetup = !state.missing && (!state.account || !state.folder);
  $("statusKind").textContent = state.busy ? "Working" : state.statusKind === "bad" ? "Attention" : state.statusKind === "warn" ? "Notice" : needsSetup ? "Get started" : "Ready";
  $("pulse").className = "pulse" + (state.busy ? " busy" : state.statusKind === "bad" ? " bad" : needsSetup ? " setup" : "");
  const disabled = !!state.busy || !!state.missing;
  for (const id of ["play", "update", "repair", "choose", "accountButton", "client", "portal", "profile", "setupPrimary", "setupSecondary"])
    $(id).disabled = disabled;
  renderAccount();
  renderSetup();
  if (state.missing) $("missingModal").classList.add("open");
}

function renderAccount() {
  const account = state.account || "";
  const signed = !!account;
  $("profile").classList.toggle("signed", signed);
  $("profileLabel").textContent = signed ? "Game account" : "No game account";
  $("profileName").textContent = signed ? account : "Create or sign in";
  $("account").textContent = signed ? account : "Not signed in";
  $("accountButton").textContent = signed ? "Change" : "Sign in";
}

function renderSetup() {
  const card = $("setupCard");
  const hasAccount = !!state.account;
  const hasFolder = !!state.folder;
  if (state.missing || (hasAccount && hasFolder)) {
    card.hidden = true;
    return;
  }
  card.hidden = false;
  const iconAccount = card.querySelector(".icon-account");
  const iconFolder = card.querySelector(".icon-folder");
  if (hasAccount) {
    iconAccount.setAttribute("hidden", "");
    iconFolder.removeAttribute("hidden");
  } else {
    iconAccount.removeAttribute("hidden");
    iconFolder.setAttribute("hidden", "");
  }
  const primary = $("setupPrimary");
  const secondary = $("setupSecondary");
  if (!hasAccount) {
    $("setupStep").textContent = "Step 1 of 2 · Game account";
    $("setupTitle").textContent = "Create your game account";
    $("setupText").textContent = "You need a game account to play on " + (state.realm || "AFK Realm") +
      ". Create one here, or sign in with an account you already have.";
    primary.textContent = "Create account";
    primary.dataset.action = "register";
    secondary.textContent = "I already have an account";
    secondary.hidden = false;
  } else {
    $("setupStep").textContent = "Step 2 of 2 · Game folder";
    $("setupTitle").textContent = "Choose your game folder";
    $("setupText").textContent = "Signed in as " + state.account +
      ". Pick the Conquest of Azeroth folder that contains Data. Updates and settings are handled automatically.";
    primary.textContent = "Choose game folder";
    primary.dataset.action = "choose";
    secondary.hidden = true;
  }
}

function shortened(path) {
  if (!path) return "";
  const parts = path.replace(/\//g, "\\").split("\\").filter(Boolean);
  if (parts.length <= 2) return path;
  return "…\\" + parts.slice(-2).join("\\");
}

function openAccount(mode) {
  clearAccountMessage();
  setAccountMode(mode || (state.account ? "login" : "register"));
  $("accountPassword").value = "";
  $("accountRepeat").value = "";
  $("accountName").value = state.account || "";
  $("accountModal").classList.add("open");
  closeSettings();
  setTimeout(() => { $("accountName").focus(); $("accountName").select(); }, 30);
}

function closeAccount() {
  $("accountModal").classList.remove("open");
  $("accountPassword").value = "";
  $("accountRepeat").value = "";
  accountPending = false;
  clearTimeout(accountTimer);
  $("accountSubmit").disabled = false;
}

function setAccountMode(mode) {
  accountMode = mode === "login" ? "login" : "register";
  const text = accountModes[accountMode];
  $("accountTitle").textContent = text.title;
  $("accountIntro").textContent = text.intro;
  $("accountSubmit").textContent = text.submit;
  $("accountNote").textContent = text.note;
  $("tabRegister").classList.toggle("active", accountMode === "register");
  $("tabLogin").classList.toggle("active", accountMode === "login");
  $("tabRegister").setAttribute("aria-selected", accountMode === "register" ? "true" : "false");
  $("tabLogin").setAttribute("aria-selected", accountMode === "login" ? "true" : "false");
  $("repeatRow").hidden = accountMode !== "register";
  $("accountPassword").autocomplete = accountMode === "register" ? "new-password" : "current-password";
}

function switchAccountMode(mode) {
  if (mode === accountMode) return;
  $("accountPassword").value = "";
  $("accountRepeat").value = "";
  clearAccountMessage();
  setAccountMode(mode);
  setTimeout(() => $("accountName").focus(), 20);
}

function accountMessage(text, ok) {
  const msg = $("accountMsg");
  msg.textContent = text || "";
  msg.className = "form-message " + (ok ? "ok" : "bad");
}

function clearAccountMessage() {
  $("accountMsg").textContent = "";
  $("accountMsg").className = "form-message";
}

function submitAccount() {
  if (accountPending) return;
  const name = $("accountName").value.trim();
  const password = $("accountPassword").value;
  const repeat = $("accountRepeat").value;
  if (!/^[A-Za-z0-9]{3,16}$/.test(name)) {
    accountMessage("The account name must be 3 to 16 characters long and may only contain letters and digits.", false);
    $("accountName").focus();
    return;
  }
  if (password.length < 4 || password.length > 16) {
    accountMessage("The password must be 4 to 16 characters long.", false);
    $("accountPassword").focus();
    return;
  }
  if (accountMode === "register" && password !== repeat) {
    accountMessage("The two passwords do not match.", false);
    $("accountRepeat").focus();
    return;
  }
  accountPending = true;
  $("accountSubmit").disabled = true;
  if (demo) {
    setTimeout(() => {
      state.account = name.toUpperCase();
      if (!state.folder) {
        state.status = "Account ready. Now choose your game folder.";
        state.statusKind = "ok";
      }
      render(state);
      accountResult({ ok: true, name: state.account, message: "Account created. Use this name and password in the game." });
    }, 450);
    return;
  }
  clearTimeout(accountTimer);
  accountTimer = setTimeout(() => {
    accountPending = false;
    $("accountSubmit").disabled = false;
    accountMessage("The server did not answer. Try again in a moment.", false);
  }, 35000);
  send(accountMode === "register" ? "register" : "login", {
    name,
    password,
    password2: accountMode === "register" ? repeat : password
  });
}

function accountResult(result) {
  if (!result) return;
  clearTimeout(accountTimer);
  accountPending = false;
  $("accountSubmit").disabled = false;
  accountMessage(result.message || "", !!result.ok);
  if (result.ok) {
    $("account").textContent = result.name || "";
    setTimeout(closeAccount, 700);
  }
}

function toast(text) {
  const t = $("toast");
  t.textContent = text;
  t.classList.add("show");
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => t.classList.remove("show"), 2600);
}

function toggleSettings() {
  const pop = $("settingsPop");
  pop.classList.toggle("open");
  $("settings").classList.toggle("active", pop.classList.contains("open"));
}
function closeSettings() {
  $("settingsPop").classList.remove("open");
  $("settings").classList.remove("active");
}

$("play").addEventListener("click", () => {
  if (!state.account) { openAccount("register"); return; }
  send("play");
});
$("update").addEventListener("click", () => { closeSettings(); send("update"); });
$("repair").addEventListener("click", () => { closeSettings(); send("repair"); });
$("settings").addEventListener("click", toggleSettings);
$("choose").addEventListener("click", () => send("choose"));
$("profile").addEventListener("click", () => openAccount(state.account ? "login" : "register"));
$("accountButton").addEventListener("click", () => openAccount(state.account ? "login" : "register"));
$("client").addEventListener("click", () => send("client"));
$("portal").addEventListener("click", () => send("portal"));
$("setupPrimary").addEventListener("click", () => {
  const action = $("setupPrimary").dataset.action;
  if (action === "choose") send("choose");
  else openAccount(action === "login" ? "login" : "register");
});
$("setupSecondary").addEventListener("click", () => openAccount("login"));
$("tabRegister").addEventListener("click", () => switchAccountMode("register"));
$("tabLogin").addEventListener("click", () => switchAccountMode("login"));
$("accountSubmit").addEventListener("click", submitAccount);
$("accountForm").addEventListener("submit", e => { e.preventDefault(); submitAccount(); });
$("closeModal").addEventListener("click", closeAccount);
$("closeMissing").addEventListener("click", () => send("close"));
$("accountModal").addEventListener("click", e => { if (e.target === $("accountModal")) closeAccount(); });
document.addEventListener("click", e => {
  if (!$("settingsPop").contains(e.target) && !$("settings").contains(e.target)) closeSettings();
});
document.addEventListener("keydown", e => {
  if (e.key === "Escape") { closeAccount(); closeSettings(); }
});

if (host) {
  host.addEventListener("message", e => {
    const message = e.data || {};
    if (message.type === "state") render(message);
    else if (message.type === "account") accountResult(message);
    else if (message.type === "accountOpen") openAccount(message.mode || "register");
  });
  send("ready");
} else {
  render({
    realm: "AFK Realm",
    folder: demoReady ? "C:\\Games\\Conquest of Azeroth" : "",
    account: demoReady ? "AzerothHero" : "",
    status: demoReady ? "Everything is up to date." : "Welcome to AFK Realm.",
    statusKind: demoReady ? "ok" : "",
    progress: demoReady ? 100 : 0,
    version: demoReady ? "20261107-074320" : "",
    portalUrl: "http://192.168.178.110:47820",
    busy: false
  });
}
