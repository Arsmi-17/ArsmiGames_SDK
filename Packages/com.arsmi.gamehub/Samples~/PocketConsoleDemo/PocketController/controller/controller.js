// Yours to edit. This is the whole protocol: postMessage up, listen for init down.
const SEAT_COLORS = ["#ff4d4d", "#ffa233", "#ffd633", "#5ad469", "#3bc9db", "#5c7cfa", "#b197fc", "#ff8cc8"];
const pressed = {};
// --- screens -----------------------------------------------------------------
// The game says which screen you are on; this shows it. Yours to edit, but keep the
// gamehub:pocket:state branch below or the game can no longer move you.
function showScreen(id, data) {
  const target = document.querySelector('[data-screen="' + id + '"]');
  if (!target) {
    // Keep whatever is showing. A typo in the game must never leave a player holding a dead
    // pad — the publish gate turns this into a hard failure before release.
    console.warn("[pocket] no screen named", id);
    return;
  }
  for (const section of document.querySelectorAll("[data-screen]")) section.hidden = section !== target;
  window.dispatchEvent(new CustomEvent("pocket:screen", { detail: { screen: id, data: data || {} } }));
}


function send(control, active) {
  pressed[control] = active;
  parent.postMessage(
    {
      type: "gamehub:pocket:input",
      input: { control, pressed: active, buttons: { ...pressed } },
    },
    "*"
  );
}

// The shell tells us which game we are driving and which seat we took; the game tells us which
// screen to show. Both arrive as window messages.
window.addEventListener("message", (event) => {
  const data = event.data;
  if (!data) return;

  if (data.type === "gamehub:pocket:state") {
    showScreen(data.screen, data.data);
    return;
  }

  if (data.type !== "gamehub:pocket:init") return;
  if (data.game?.title) document.getElementById("game-title").textContent = data.game.title;
  if (data.playerSlot) {
    document.getElementById("slot").textContent = "P" + data.playerSlot;
    // Same table the game reads, indexed the same way, so this colour identifies this seat in
    // both places without either side being told what the other chose.
    const color = SEAT_COLORS[(Math.max(1, data.playerSlot) - 1) % SEAT_COLORS.length];
    document.documentElement.style.setProperty("--seat-color", color);
  }
});

document.querySelector("[data-back]")?.addEventListener("click", () => {
  parent.postMessage({ type: "gamehub:pocket:back" }, "*");
});

for (const button of document.querySelectorAll("[data-control]")) {
  const control = button.getAttribute("data-control");

  const down = (event) => {
    event.preventDefault();
    button.classList.add("active");
    // Pointer capture keeps the release ours even if the thumb slides off the button.
    try { button.setPointerCapture(event.pointerId); } catch {}
    send(control, true);
  };
  const up = (event) => {
    event.preventDefault();
    button.classList.remove("active");
    try { button.releasePointerCapture(event.pointerId); } catch {}
    send(control, false);
  };

  button.addEventListener("pointerdown", down);
  button.addEventListener("pointerup", up);
  button.addEventListener("pointercancel", up);
  button.addEventListener("lostpointercapture", () => {
    if (pressed[control]) send(control, false);
  });
}
