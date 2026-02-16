using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DeliveryControl.Filters
{
    public class AuthorizeAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Skip check for AccountController (Login/Logout)
            var controllerName = context.RouteData?.Values["controller"]?.ToString();
            if (string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Check for AllowAnonymous attribute
            var hasAllowAnonymous = context.ActionDescriptor.EndpointMetadata
                .Any(em => em.GetType().Name == "AllowAnonymousAttribute");

            if (hasAllowAnonymous)
            {
                return;
            }

            var session = context.HttpContext.Session;
            if (session == null) 
            {
                 context.Result = new RedirectToActionResult("Login", "Account", null);
                 return;
            }

            var userId = session.GetString("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                // Jika belum login, redirect ke login
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }

    public class AuthorizeAdminAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var session = context.HttpContext.Session;
            var userId = session.GetString("UserId");
            var role = session.GetString("Role");

            if (string.IsNullOrEmpty(userId))
            {
                // Jika belum login, redirect ke login
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            if (role != "Admin")
            {
                // Jika bukan admin, redirect ke home dengan error message
                context.HttpContext.Session.SetString("ErrorMessage", "Anda tidak memiliki akses untuk halaman ini");
                context.Result = new RedirectToActionResult("Index", "Home", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}

