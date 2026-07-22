(function () {
  "use strict";

  function waitForMessage(identityHostUrl, sourceWindow) {
    return new Promise(function (resolve, reject) {
      function onMessage(event) {
        if (event.source !== sourceWindow) return;
        if (event.origin !== new URL(identityHostUrl).origin) return;
        if (event.data?.source !== "synckit-auth") return;
        window.removeEventListener("message", onMessage);
        if (event.data.error) {
          reject(new Error(event.data.error));
        } else {
          resolve({ code: event.data.code });
        }
      }
      window.addEventListener("message", onMessage);
    });
  }

  function login(identityHostUrl, provider) {
    return new Promise(function (resolve, reject) {
      var returnUrl = window.location.href;
      var goUrl = identityHostUrl.replace(/\/$/, "") + "/login/go/" + encodeURIComponent(provider) + "?returnUrl=" + encodeURIComponent(returnUrl);
      var popup = window.open(goUrl, "synckit-login", "width=480,height=640");

      if (!popup) {
        reject(new Error("popup_blocked"));
        return;
      }

      var settled = false;
      waitForMessage(identityHostUrl, popup).then(
        function (result) { settled = true; resolve(result); },
        function (err) { settled = true; reject(err); }
      );

      var closeCheck = setInterval(function () {
        if (popup.closed) {
          clearInterval(closeCheck);
          if (!settled) reject(new Error("popup_closed"));
        } else if (settled) {
          clearInterval(closeCheck);
        }
      }, 500);
    });
  }

  window.SyncKitAuth = { login: login };
})();
