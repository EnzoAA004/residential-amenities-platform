using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResidentialAmenities.Api.Infrastructure.Persistence;
using ResidentialAmenities.Api.Modules.Audit.Application;
using ResidentialAmenities.Api.Modules.Audit.Domain;
using ResidentialAmenities.Api.Modules.Buildings.Domain;
using ResidentialAmenities.Api.Modules.Identity.Application;
using ResidentialAmenities.Api.Modules.Identity.Domain;

namespace ResidentialAmenities.Api.Modules.Identity;

/// <summary>
/// Resident invitation and email verification (issue #93, DEC-014/OQ-015):
/// resident accounts are Administrator-created only, never public self-
/// service registration. An Administrator supplies building/unit/email
/// only — they never learn, store or send the resident's final password.
/// The resident verifies a single-use code emailed to them and sets their
/// own initial password; the same code/verification mechanism backs a
/// later forgotten-password flow.
/// </summary>
public static class InvitationEndpoints
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    private const string GenericVerificationFailure =
        "The verification code is invalid or has expired.";

    private const string GenericForgotPasswordResponse =
        "If an account exists for this email, instructions were sent.";

    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost("/api/admin/residents", InviteResidentAsync)
            .WithTags("Identity")
            .RequireAuthorization(AuthorizationPolicies.Administrator);

        var authGroup = endpoints.MapGroup("/api/auth").WithTags("Identity");

        authGroup.MapPost("/activate", ActivateAsync).AllowAnonymous();
        authGroup.MapPost("/forgot-password", ForgotPasswordAsync).AllowAnonymous();
        authGroup.MapPost("/reset-password", ResetPasswordAsync).AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> InviteResidentAsync(
        InviteResidentRequest request,
        ClaimsPrincipal principal,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IEmailSender emailSender,
        IAuditRecorder auditRecorder,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } actorUserId)
        {
            return Results.Unauthorized();
        }

        if (request.BuildingId == Guid.Empty || request.UnitId == Guid.Empty)
        {
            return ValidationProblem("unitId", "buildingId and unitId are required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return ValidationProblem("email", "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ValidationProblem("displayName", "Display name is required.");
        }

        var unit = await dbContext.Units
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.UnitId && candidate.BuildingId == request.BuildingId,
                cancellationToken);

        if (unit is null)
        {
            return ValidationProblem("unitId", "Unit not found for this building.");
        }

        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return ValidationProblem("email", "An account with this email already exists.");
        }

        // No password is ever set here — CreateAsync(user) with no password
        // leaves PasswordHash null, so the account cannot sign in until the
        // resident verifies their code and sets their own password below.
        var user = new UserAccount(Guid.NewGuid(), request.Email, request.DisplayName)
        {
            EmailConfirmed = false
        };

        var createResult = await userManager.CreateAsync(user);

        if (!createResult.Succeeded)
        {
            return Results.ValidationProblem(ToErrors(createResult));
        }

        await userManager.AddToRoleAsync(user, ApplicationRoles.Resident);

        var nowUtc = timeProvider.GetUtcNow();

        dbContext.ResidentMemberships.Add(
            new ResidentMembership(Guid.NewGuid(), request.BuildingId, request.UnitId, user.Id, nowUtc));

        var code = VerificationCodeGenerator.GenerateCode();

        dbContext.VerificationCodes.Add(new VerificationCode(
            Guid.NewGuid(),
            user.Id,
            VerificationCodePurpose.AccountActivation,
            VerificationCodeGenerator.Hash(code),
            nowUtc,
            nowUtc + CodeLifetime));

        auditRecorder.Record(AuditRecord.ByUser(
            actorUserId, AuditAction.ResidentInvited, AuditTargetType.User, user.Id, request.BuildingId));

        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            request.Email,
            "Activate your resident account",
            $"Your verification code is {code}. It expires in {CodeLifetime.TotalMinutes:0} minutes.",
            cancellationToken);

        return Results.Created(
            $"/api/admin/residents/{user.Id}",
            new InviteResidentResponse(user.Id, user.Email!, user.DisplayName));
    }

    private static async Task<IResult> ActivateAsync(
        ActivateRequest request,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IAuditRecorder auditRecorder,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return ValidationProblem("newPassword", "A new password is required.");
        }

        var user = await userManager.FindByEmailAsync(request.Email ?? string.Empty);

        if (user is null)
        {
            return Results.Problem(title: GenericVerificationFailure, statusCode: StatusCodes.Status400BadRequest);
        }

        var nowUtc = timeProvider.GetUtcNow();

        var verification = await FindLatestCodeAsync(
            dbContext, user.Id, VerificationCodePurpose.AccountActivation, cancellationToken);

        if (verification is null || !verification.IsUsable(nowUtc))
        {
            return Results.Problem(title: GenericVerificationFailure, statusCode: StatusCodes.Status400BadRequest);
        }

        if (VerificationCodeGenerator.Hash(request.Code ?? string.Empty) != verification.CodeHash)
        {
            verification.RegisterFailedAttempt();
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Problem(title: GenericVerificationFailure, statusCode: StatusCodes.Status400BadRequest);
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var setPasswordResult = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!setPasswordResult.Succeeded)
        {
            return Results.ValidationProblem(ToErrors(setPasswordResult));
        }

        verification.Consume(nowUtc);
        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);

        auditRecorder.Record(AuditRecord.ByUser(
            user.Id, AuditAction.ResidentActivated, AuditTargetType.User, user.Id, buildingId: null));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IEmailSender emailSender,
        IAuditRecorder auditRecorder,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Anti-enumeration: the response is identical regardless of whether
        // the email belongs to a real account.
        var user = await userManager.FindByEmailAsync(request.Email ?? string.Empty);

        if (user is not null)
        {
            var nowUtc = timeProvider.GetUtcNow();
            var code = VerificationCodeGenerator.GenerateCode();

            dbContext.VerificationCodes.Add(new VerificationCode(
                Guid.NewGuid(),
                user.Id,
                VerificationCodePurpose.PasswordReset,
                VerificationCodeGenerator.Hash(code),
                nowUtc,
                nowUtc + CodeLifetime));

            auditRecorder.Record(AuditRecord.ByUser(
                user.Id, AuditAction.PasswordResetRequested, AuditTargetType.User, user.Id, buildingId: null));

            await dbContext.SaveChangesAsync(cancellationToken);

            await emailSender.SendAsync(
                user.Email!,
                "Reset your password",
                $"Your verification code is {code}. It expires in {CodeLifetime.TotalMinutes:0} minutes.",
                cancellationToken);
        }

        return Results.Ok(new { message = GenericForgotPasswordResponse });
    }

    private static async Task<IResult> ResetPasswordAsync(
        ActivateRequest request,
        UserManager<UserAccount> userManager,
        AppDbContext dbContext,
        IAuditRecorder auditRecorder,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return ValidationProblem("newPassword", "A new password is required.");
        }

        var user = await userManager.FindByEmailAsync(request.Email ?? string.Empty);

        if (user is null)
        {
            return Results.Problem(title: GenericVerificationFailure, statusCode: StatusCodes.Status400BadRequest);
        }

        var nowUtc = timeProvider.GetUtcNow();

        var verification = await FindLatestCodeAsync(
            dbContext, user.Id, VerificationCodePurpose.PasswordReset, cancellationToken);

        if (verification is null || !verification.IsUsable(nowUtc))
        {
            return Results.Problem(title: GenericVerificationFailure, statusCode: StatusCodes.Status400BadRequest);
        }

        if (VerificationCodeGenerator.Hash(request.Code ?? string.Empty) != verification.CodeHash)
        {
            verification.RegisterFailedAttempt();
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Problem(title: GenericVerificationFailure, statusCode: StatusCodes.Status400BadRequest);
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var setPasswordResult = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!setPasswordResult.Succeeded)
        {
            return Results.ValidationProblem(ToErrors(setPasswordResult));
        }

        verification.Consume(nowUtc);

        auditRecorder.Record(AuditRecord.ByUser(
            user.Id, AuditAction.PasswordResetCompleted, AuditTargetType.User, user.Id, buildingId: null));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static Task<VerificationCode?> FindLatestCodeAsync(
        AppDbContext dbContext, Guid userId, VerificationCodePurpose purpose, CancellationToken cancellationToken) =>
        dbContext.VerificationCodes
            .Where(code => code.UserId == userId && code.Purpose == purpose)
            .OrderByDescending(code => code.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static Dictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());

    private static IResult ValidationProblem(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [key] = [message]
        });

    private sealed record InviteResidentRequest(
        Guid BuildingId, Guid UnitId, string Email, string DisplayName);

    private sealed record InviteResidentResponse(Guid Id, string Email, string DisplayName);

    private sealed record ActivateRequest(string? Email, string? Code, string NewPassword);

    private sealed record ForgotPasswordRequest(string? Email);
}
