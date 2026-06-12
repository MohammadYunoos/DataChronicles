// Helpers for Azure App Service Authentication ("Easy Auth").
// When SSO is enabled on the App Service, the platform exposes these endpoints with
// no backend code: /.auth/me (current user), /.auth/login/aad, /.auth/logout.
// Locally (Easy Auth off) /.auth/me 404s, so getMe() returns null and the UI shows nothing.

export interface AuthUser {
  name: string;
  email?: string;
}

interface ClientPrincipalClaim {
  typ: string;
  val: string;
}

export async function getMe(): Promise<AuthUser | null> {
  try {
    const res = await fetch('/.auth/me', { headers: { Accept: 'application/json' } });
    if (!res.ok) return null; // 404 locally / 401 when not signed in
    const data = await res.json();
    const principal = Array.isArray(data) ? data[0] : data?.clientPrincipal;
    if (!principal) return null;

    const claims: ClientPrincipalClaim[] = principal.user_claims ?? principal.claims ?? [];
    const claim = (types: string[]) =>
      claims.find((c) => types.includes(c.typ))?.val;

    const name =
      principal.user_id ||
      claim(['name', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name']) ||
      claim(['preferred_username', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress']) ||
      'Signed in';
    const email = claim([
      'preferred_username',
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress',
      'emails',
    ]);

    return { name, email };
  } catch {
    return null; // network/parse issues -> treat as not signed in
  }
}

export const logoutUrl = '/.auth/logout?post_logout_redirect_uri=/';
