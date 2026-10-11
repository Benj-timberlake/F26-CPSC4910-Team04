// webauthn needs https or localhost
window.truckerPasskeys = (() => {
    const toBytes = s => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0));
    const toText = b => btoa(String.fromCharCode(...new Uint8Array(b))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

    async function post(url, body) {
        const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });
        const json = await response.json().catch(() => ({}));
        return { ok: response.ok, json };
    }

    const supported = () => !!window.PublicKeyCredential && window.isSecureContext;

    async function register(name) {
        if (!supported()) return 'This browser or address can\'t use passkeys.';
        const begin = await post('account/passkeys/options');
        if (!begin.ok) return begin.json.message ?? 'Couldn\'t start. Try again.';
        const o = begin.json.options;
        let credential;
        try {
            credential = await navigator.credentials.create({
                publicKey: {
                    ...o,
                    challenge: toBytes(o.challenge),
                    user: { ...o.user, id: toBytes(o.user.id) },
                    excludeCredentials: (o.excludeCredentials ?? []).map(c => ({ ...c, id: toBytes(c.id) }))
                }
            });
        } catch {
            return 'Cancelled, or this device already has a passkey here.';
        }
        const r = credential.response;
        const done = await post('account/passkeys', {
            requestId: begin.json.requestId,
            name,
            response: {
                id: credential.id, rawId: toText(credential.rawId), type: credential.type, clientExtensionResults: {},
                response: { attestationObject: toText(r.attestationObject), clientDataJSON: toText(r.clientDataJSON), transports: r.getTransports?.() ?? [] }
            }
        });
        return done.ok ? null : done.json.message ?? 'That passkey couldn\'t be saved.';
    }

    async function signIn(returnUrl, status) {
        const say = text => { const el = document.getElementById(status); if (el) { el.textContent = text; el.hidden = !text; } };
        say('');
        if (!supported()) return say('This browser or address can\'t use passkeys.');
        const begin = await post('account/passkey-login/options');
        if (!begin.ok) return say('Couldn\'t start. Try again.');
        const o = begin.json.options;
        let assertion;
        try {
            assertion = await navigator.credentials.get({
                publicKey: { ...o, challenge: toBytes(o.challenge), allowCredentials: (o.allowCredentials ?? []).map(c => ({ ...c, id: toBytes(c.id) })) }
            });
        } catch {
            return say('Sign in with a passkey was cancelled.');
        }
        const r = assertion.response;
        const done = await post('account/passkey-login', {
            requestId: begin.json.requestId,
            returnUrl,
            response: {
                id: assertion.id, rawId: toText(assertion.rawId), type: assertion.type, clientExtensionResults: {},
                response: {
                    authenticatorData: toText(r.authenticatorData), clientDataJSON: toText(r.clientDataJSON),
                    signature: toText(r.signature), userHandle: r.userHandle ? toText(r.userHandle) : null
                }
            }
        });
        if (done.ok) window.location.href = done.json.redirect;
        else say(done.json.message ?? 'That passkey didn\'t work.');
    }

    return { supported, register, signIn };
})();
