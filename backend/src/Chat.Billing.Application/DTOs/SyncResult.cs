namespace Chat.Billing.Application.DTOs;

/// <summary>What a sync did with a subscription read from the payment provider.</summary>
public enum SyncResult
{
    Saved,
    NotTiedToUser,
    UnknownPlan,
    MissingAtGateway,

    /// <summary>The user's newer subscription grants a plan and this older one does not.</summary>
    Superseded,
}
