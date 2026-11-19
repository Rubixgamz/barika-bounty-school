const sessionKey = "barikaBountySession";
const portalPaths = { Admin: "/admin.html", OrganizationOwner: "/organization.html", Innovator: "/user.html", Evaluator: "/user.html" };
const heading = document.getElementById("authHeading");
const status = document.getElementById("authStatus");
const home = document.getElementById("authHome");

async function completeSignIn() {
    if (new URLSearchParams(window.location.search).get("pending") === "1") {
        heading.textContent = "Registration received.";
        status.textContent = "Your organization is awaiting administrator approval. You can sign in once it has been approved.";
        home.hidden = false;
        return;
    }

    try {
        const response = await fetch("/api/auth/external/complete", {
            method: "POST",
            credentials: "same-origin"
        });
        const body = response.status === 401 ? "" : await response.text();
        let result = null;
        try {
            result = body ? JSON.parse(body) : null;
        } catch {
            result = body;
        }
        if (response.status === 403 && result?.approvalPending) {
            heading.textContent = "Approval pending.";
            status.textContent = result.message;
            home.hidden = false;
            return;
        }
        if (response.status === 202 && result?.requiresApproval) {
            heading.textContent = "Registration received.";
            status.textContent = result.message;
            home.hidden = false;
            return;
        }
        if (!response.ok) throw new Error(typeof result === "string" ? result : result?.message || "Social sign-in could not be completed. Try again or use email and password.");

        sessionStorage.setItem(sessionKey, JSON.stringify({ token: result.token, user: result.user }));
        window.location.replace(portalPaths[result.user.role] || "/");
    } catch (error) {
        heading.textContent = "Sign-in could not be completed.";
        status.textContent = error instanceof TypeError
            ? "Could not reach the sign-in service. Please try again."
            : error.message;
        home.hidden = false;
    }
}

completeSignIn();
