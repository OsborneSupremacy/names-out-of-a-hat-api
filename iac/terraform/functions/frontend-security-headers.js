// Adds the security headers every response from the site should carry.
//
// A function rather than a response headers policy because the distribution is on a CloudFront
// flat-rate plan, and custom response header policies start at the Business tier. The AWS-managed
// SecurityHeadersPolicy the plan does allow sets no Content-Security-Policy, and that is the header
// that matters most here: the session token sits in localStorage for two weeks, so anything that
// ever ran script on this origin could take it.
//
// What the policy allows, and why:
//   script-src 'self'           -- Vite emits one module script and nothing inline.
//   style-src 'self'            -- one stylesheet; React's style props go through the CSSOM, which
//                                  CSP does not govern.
//   style-src-attr 'unsafe-inline'
//                               -- the invitation preview renders the server's email HTML, whose
//                                  styles are inline attributes. No <style> blocks, no script.
//   connect-src ... api.        -- the API, and nothing else.
//   frame-ancestors 'none'      -- nobody frames the site to click through it.
//
// If a new dependency needs more, the browser console names the directive that refused it.
function handler(event) {
    var headers = event.response.headers;

    headers['content-security-policy'] = {
        value: "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self'; " +
            "style-src-attr 'unsafe-inline'; " +
            "img-src 'self'; " +
            "font-src 'self'; " +
            "connect-src 'self' https://api.namesoutofahat.com; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'"
    };
    headers['strict-transport-security'] = { value: 'max-age=31536000; includeSubDomains' };
    headers['x-content-type-options'] = { value: 'nosniff' };
    headers['x-frame-options'] = { value: 'DENY' };
    headers['referrer-policy'] = { value: 'no-referrer' };

    return event.response;
}
