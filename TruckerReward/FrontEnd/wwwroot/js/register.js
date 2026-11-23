window.truckerRegister = {
    async submit(event, form) {
        event.preventDefault();
        const button = form.querySelector('button[type="submit"]');
        const error = document.getElementById('register-submit-error');
        const originalText = button?.textContent;
        if (error) {
            error.hidden = true;
            error.textContent = '';
        }
        if (button) {
            button.disabled = true;
            button.textContent = 'Creating account...';
        }

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'application/json'
                }
            });
            const result = await response.json().catch(() => ({}));
            if (!response.ok) {
                if (error) {
                    error.textContent = result.message || 'Could not create the account. Check your information and try again.';
                    error.hidden = false;
                }
                return false;
            }
            window.location.assign(result.redirectUrl || '/dashboard');
        } catch {
            if (error) {
                error.textContent = 'Could not reach the server. Your entries are still here; please try again.';
                error.hidden = false;
            }
        } finally {
            if (button) {
                button.disabled = false;
                button.textContent = originalText;
            }
        }
        return false;
    }
};
