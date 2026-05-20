namespace FSI.Trade.Compliance.Application.Features.Auth.TwoFactor.Enable;

/// <summary>
/// Returned by EnableTwoFactor. The FE renders <see cref="ProvisioningUri"/>
/// as a QR code (using a JS QR library) and shows <see cref="Secret"/> as
/// a fallback for manual entry into the authenticator app.
/// </summary>
public class EnableTwoFactorResponse
{
    public string Secret          { get; set; } = "";
    public string ProvisioningUri { get; set; } = "";
}
