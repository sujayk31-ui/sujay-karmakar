(function () {
  const unavailable = "This device has no platform authenticator, so fingerprint or face sign-in is not available here. Use your user ID and password, or your PIN.";
  const noApi = "This browser cannot use fingerprint or face sign-in. Use your user ID and password, or your PIN.";

  function token() {
    return document.querySelector('meta[name="request-verification-token"]')?.content || "";
  }

  function bytesToBase64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let text = "";
    for (const byte of bytes) text += String.fromCharCode(byte);
    return btoa(text).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
  }

  function base64UrlToBytes(value) {
    if (value == null) return new ArrayBuffer(0);
    if (typeof value !== "string") return new Uint8Array(value).buffer;
    const padded = value.replace(/-/g, "+").replace(/_/g, "/");
    const pad = "=".repeat((4 - (padded.length % 4)) % 4);
    const binary = atob(padded + pad);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes.buffer;
  }

  async function post(url, body) {
    const response = await fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        "RequestVerificationToken": token()
      },
      body: JSON.stringify(body ?? {})
    });
    const text = await response.text();
    try { return JSON.parse(text); }
    catch { return { status: "error", errorMessage: text || "The sign-in service did not answer." }; }
  }

  async function platformReady(status) {
    if (!window.PublicKeyCredential) {
      status.textContent = noApi;
      return false;
    }
    let available = false;
    try {
      available = await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
    } catch {
      available = false;
    }
    if (!available) {
      status.textContent = unavailable;
      return false;
    }
    return true;
  }

  function publicKey(options) {
    const key = {
      challenge: base64UrlToBytes(options.challenge),
      timeout: options.timeout,
      rpId: options.rpId || options.rp?.id
    };
    if (options.rp) key.rp = options.rp;
    if (options.user) {
      key.user = {
        id: base64UrlToBytes(options.user.id),
        name: options.user.name,
        displayName: options.user.displayName
      };
    }
    if (options.pubKeyCredParams) key.pubKeyCredParams = options.pubKeyCredParams;
    if (options.attestation) key.attestation = options.attestation;
    if (options.authenticatorSelection) key.authenticatorSelection = options.authenticatorSelection;
    if (options.userVerification) key.userVerification = options.userVerification;
    const excluded = options.excludeCredentials || [];
    if (excluded.length) {
      key.excludeCredentials = excluded.map((item) => ({ type: item.type, id: base64UrlToBytes(item.id) }));
    }
    const allowed = options.allowCredentials || [];
    if (allowed.length) {
      key.allowCredentials = allowed.map((item) => ({ type: item.type, id: base64UrlToBytes(item.id) }));
    }
    return key;
  }

  function cancelled(error) {
    return error && (error.name === "NotAllowedError" || error.name === "AbortError");
  }

  async function signIn(status) {
    if (!await platformReady(status)) return;
    try {
      const options = await post("/webauthn/login-options", {});
      if (options.status === "error") {
        status.textContent = options.errorMessage;
        return;
      }
      const credential = await navigator.credentials.get({ publicKey: publicKey(options) });
      const result = await post("/webauthn/login", {
        id: credential.id,
        rawId: bytesToBase64Url(credential.rawId),
        type: credential.type,
        response: {
          authenticatorData: bytesToBase64Url(credential.response.authenticatorData),
          clientDataJSON: bytesToBase64Url(credential.response.clientDataJSON),
          signature: bytesToBase64Url(credential.response.signature),
          userHandle: credential.response.userHandle ? bytesToBase64Url(credential.response.userHandle) : null
        }
      });
      if (result.status === "ok") {
        window.location.assign("/");
        return;
      }
      status.textContent = result.errorMessage || "Fingerprint sign-in was not verified.";
    } catch (error) {
      status.textContent = cancelled(error)
        ? "The fingerprint check was cancelled."
        : (error.message || "Fingerprint sign-in failed.");
    }
  }

  async function enroll(status) {
    if (!await platformReady(status)) return;
    try {
      const options = await post("/webauthn/register-options", {});
      if (options.status === "error") {
        status.textContent = options.errorMessage;
        return;
      }
      const credential = await navigator.credentials.create({ publicKey: publicKey(options) });
      const label = document.getElementById("device-label")?.value || "This device";
      const result = await post("/webauthn/register", {
        deviceLabel: label,
        credential: {
          id: credential.id,
          rawId: bytesToBase64Url(credential.rawId),
          type: credential.type,
          response: {
            attestationObject: bytesToBase64Url(credential.response.attestationObject),
            clientDataJSON: bytesToBase64Url(credential.response.clientDataJSON)
          }
        }
      });
      if (result.status === "ok") {
        window.location.reload();
        return;
      }
      status.textContent = result.errorMessage || "Fingerprint enrollment was not verified.";
    } catch (error) {
      status.textContent = cancelled(error)
        ? "The fingerprint check was cancelled."
        : (error.message || "Fingerprint enrollment failed.");
    }
  }

  const loginStatus = document.getElementById("passkey-status");
  const enrollStatus = document.getElementById("enroll-status");
  if (loginStatus) platformReady(loginStatus);
  if (enrollStatus) platformReady(enrollStatus);
  document.getElementById("passkey-login")?.addEventListener("click", () => signIn(loginStatus));
  document.getElementById("passkey-enroll")?.addEventListener("click", () => enroll(enrollStatus));
})();
