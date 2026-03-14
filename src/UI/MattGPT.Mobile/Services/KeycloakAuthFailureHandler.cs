using MattGPT.ApiClient.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace MattGPT.Mobile.Services;

internal class KeycloakAuthFailureHandler(KeycloakAuthService authService) : IAuthFailureHandler
{
    public Task<bool> HandleAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
