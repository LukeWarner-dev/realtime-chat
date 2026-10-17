"use strict";

const $ = (id) => document.getElementById(id);

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/chat")
  .withAutomaticReconnect()
  .build();

let me = null;          // my display name
let currentRoom = null;
let rooms = [];
const typingUsers = new Set();

// ---- join screen ----

function renderRoomOptions() {
  const select = $("roomSelect");
  const selected = select.value;
  select.innerHTML = "";
  for (const room of rooms) {
    select.add(new Option(`${room.name} (${room.userCount})`, room.name));
  }
  select.add(new Option("+ Create a new room", "__new"));
  if ([...select.options].some((o) => o.value === selected)) select.value = selected;
  $("newRoomInput").hidden = select.value !== "__new";
}

$("roomSelect").addEventListener("change", () => {
  const creating = $("roomSelect").value === "__new";
  $("newRoomInput").hidden = !creating;
  if (creating) $("newRoomInput").focus();
});

$("joinForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = $("nameInput").value.trim();
  const room = $("roomSelect").value === "__new" ? $("newRoomInput").value.trim() : $("roomSelect").value;
  if (!name || !room) return;

  $("joinError").textContent = "";
  try {
    await joinRoom(name, room);
    me = name;
    $("meName").textContent = name;
    $("joinScreen").hidden = true;
    $("chatScreen").hidden = false;
    $("messageInput").focus();
  } catch (err) {
    $("joinError").textContent = cleanError(err);
  }
});

// ---- rooms ----

async function joinRoom(name, room) {
  const result = await connection.invoke("Join", name, room);
  currentRoom = result.room;
  typingUsers.clear();
  renderTyping();

  $("roomTitle").textContent = `# ${currentRoom}`;
  $("messageList").innerHTML = "";
  result.history.forEach(addMessage);
  renderUsers(result.users);
  renderRoomList();
}

function renderRoomList() {
  const list = $("roomList");
  list.innerHTML = "";
  for (const room of rooms) {
    const li = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = `# ${room.name}`;
    if (room.name === currentRoom) button.classList.add("active");
    button.addEventListener("click", () => switchRoom(room.name));

    const count = document.createElement("span");
    count.className = "count";
    count.textContent = room.userCount;
    button.append(count);

    li.append(button);
    list.append(li);
  }
}

async function switchRoom(room) {
  if (room === currentRoom) return;
  try {
    await joinRoom(me, room);
  } catch (err) {
    alertInChat(cleanError(err));
  }
}

$("newRoomButton").addEventListener("click", async () => {
  const room = prompt("Name for the new room:");
  if (room && room.trim()) await switchRoom(room.trim());
});

// ---- messages ----

function addMessage(message) {
  const list = $("messageList");
  const nearBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 80;

  const li = document.createElement("li");

  if (message.isSystem) {
    li.className = "system";
    li.textContent = message.text;
  } else {
    li.className = message.user === me ? "message mine" : "message";

    const avatar = document.createElement("span");
    avatar.className = "avatar";
    avatar.textContent = message.user.charAt(0).toUpperCase();
    avatar.style.background = colourFor(message.user);

    const body = document.createElement("div");
    const meta = document.createElement("div");
    meta.className = "meta";
    const author = document.createElement("strong");
    author.textContent = message.user;
    const time = document.createElement("time");
    time.dateTime = message.sentAt;
    time.textContent = new Date(message.sentAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    meta.append(author, time);

    // textContent (never innerHTML) so nobody can inject HTML into other people's browsers
    const text = document.createElement("p");
    text.textContent = message.text;

    body.append(meta, text);
    li.append(avatar, body);
  }

  list.append(li);
  if (nearBottom || message.user === me) list.scrollTop = list.scrollHeight;
}

function alertInChat(text) {
  addMessage({ isSystem: true, text });
}

$("messageForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const input = $("messageInput");
  const text = input.value.trim();
  if (!text) return;

  input.value = "";
  stopTyping();
  try {
    await connection.invoke("SendMessage", text);
  } catch (err) {
    input.value = text; // give it back so it isn't lost
    alertInChat(cleanError(err));
  }
});

// ---- users and typing ----

function renderUsers(users) {
  const list = $("userList");
  list.innerHTML = "";
  for (const user of users) {
    const li = document.createElement("li");
    const dot = document.createElement("span");
    dot.className = "dot";
    dot.style.background = colourFor(user);
    li.append(dot, document.createTextNode(user === me ? `${user} (you)` : user));
    list.append(li);
  }
}

function renderTyping() {
  const names = [...typingUsers];
  let text = "";
  if (names.length === 1) text = `${names[0]} is typing…`;
  else if (names.length === 2) text = `${names[0]} and ${names[1]} are typing…`;
  else if (names.length > 2) text = "Several people are typing…";
  $("typingIndicator").textContent = text;
}

// Send "typing" once when you start, and "stopped" after 2 seconds of no keys,
// instead of a message on every keystroke.
let typingTimer = null;
$("messageInput").addEventListener("input", () => {
  if (!typingTimer) connection.invoke("Typing", true).catch(() => {});
  clearTimeout(typingTimer);
  typingTimer = setTimeout(stopTyping, 2000);
});

function stopTyping() {
  if (!typingTimer) return;
  clearTimeout(typingTimer);
  typingTimer = null;
  connection.invoke("Typing", false).catch(() => {});
}

// ---- server events ----

connection.on("ReceiveMessage", (message) => {
  if (message.room !== currentRoom) return;
  typingUsers.delete(message.user);
  renderTyping();
  addMessage(message);
});

connection.on("UsersChanged", (room, users) => {
  if (room === currentRoom) renderUsers(users);
});

connection.on("RoomsChanged", (list) => {
  rooms = list;
  renderRoomOptions();
  renderRoomList();
});

connection.on("UserTyping", (user, isTyping) => {
  if (isTyping) typingUsers.add(user);
  else typingUsers.delete(user);
  renderTyping();
});

// ---- connection lifecycle ----

function setStatus(text, cls) {
  const el = $("connectionStatus");
  el.textContent = text;
  el.className = `status ${cls}`;
}

connection.onreconnecting(() => setStatus("reconnecting…", "away"));

connection.onreconnected(async () => {
  setStatus("online", "online");
  // the server forgot us when the old connection dropped, so join again
  if (me && currentRoom) {
    try {
      await joinRoom(me, currentRoom);
    } catch (err) {
      alertInChat(cleanError(err));
    }
  }
});

connection.onclose(() => setStatus("offline", "offline"));

async function start() {
  try {
    await connection.start();
    $("joinButton").disabled = false;
    $("joinButton").textContent = "Join chat";
  } catch {
    $("joinButton").textContent = "Can't reach the server";
    setTimeout(start, 3000);
  }
}

// ---- helpers ----

// HubException messages arrive wrapped in extra text, so strip that off
function cleanError(err) {
  const text = String(err?.message ?? err);
  const match = text.match(/HubException: (.*)$/);
  return match ? match[1] : "Something went wrong. Please try again.";
}

// same name always gets the same colour
function colourFor(name) {
  let hash = 0;
  for (const ch of name.toLowerCase()) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  return `hsl(${Math.abs(hash) % 360} 55% 45%)`;
}

start();
