using System.Security.Claims;
using Cloud_Image_Uploader.Models;
using Cloud_Image_Uploader.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cloud_Image_Uploader.Controllers;

public class AccountController : Controller
{
    private const string ForgotPasswordSuccessMessage = "If that account exists, a password reset email has been sent.";

    private readonly UserAccountService _userAccountService;
    private readonly PasswordResetEmailService _passwordResetEmailService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserAccountService userAccountService,
        PasswordResetEmailService passwordResetEmailService,
        ILogger<AccountController> logger)
    {
        _userAccountService = userAccountService;
        _passwordResetEmailService = passwordResetEmailService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userAccountService.AuthenticateAsync(model.Identifier, model.Password);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid username/email or password.");
            return View(model);
        }

        await SignInAsync(user, model.RememberMe);
        _logger.LogInformation("User logged in: {UserId}", user.UserId);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new RegisterViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _userAccountService.RegisterAsync(model.UserName, model.Email, model.Password);
        if (!result.Success || result.User == null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Could not create account.");
            return View(model);
        }

        await SignInAsync(result.User, isPersistent: true);
        _logger.LogInformation("User registered and logged in: {UserId}", result.User.UserId);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        TempData["Success"] = ForgotPasswordSuccessMessage;

        var (token, recipientEmail) = await _userAccountService.CreatePasswordResetTokenAsync(model.Identifier);
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(recipientEmail))
        {
            var resetUrl = Url.Action("ResetPassword", "Account", new { token }, Request.Scheme);
            if (string.IsNullOrWhiteSpace(resetUrl))
            {
                TempData["Error"] = "The password reset link could not be generated.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var emailResult = await _passwordResetEmailService.SendPasswordResetAsync(recipientEmail, resetUrl);
            if (!emailResult.Success && !string.IsNullOrWhiteSpace(emailResult.ErrorMessage))
            {
                TempData["Error"] = emailResult.ErrorMessage;
            }

            if (!string.IsNullOrWhiteSpace(emailResult.DevelopmentResetLink))
            {
                TempData["ResetLink"] = emailResult.DevelopmentResetLink;
                TempData["Success"] = "Password reset email preview is enabled in development, so the link is shown below.";
            }
        }

        return RedirectToAction(nameof(ForgotPassword));
    }

    [HttpGet]
    public IActionResult ResetPassword(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData["Error"] = "Reset token is missing.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(new ResetPasswordViewModel { Token = token });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _userAccountService.ResetPasswordAsync(model.Token, model.Password);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["Success"] = "Password reset successful. You can now log in.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    private async Task SignInAsync(UserAccount user, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.UserName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(isPersistent ? 14 : 1)
            });
    }
}