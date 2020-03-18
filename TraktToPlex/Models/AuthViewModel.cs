using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace TraktToPlex.Models
{
    public class AuthViewModel
    {
        public string PlexKey { get; set; }
        public string PlexServerKey { get; set; }
        public List<SelectListItem> PlexServers { get; set; }
        public string TraktKey { get; set; }
    }
}