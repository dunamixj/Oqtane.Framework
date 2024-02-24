using System.Net;
using System.Xml.Linq;
using System;
using Oqtane.Models;

namespace Oqtane.Shared
{
    // this class is used for passing state between components and services on the client
    public class SiteState
    {
        public Alias Alias { get; set; }
        public string AntiForgeryToken { get; set; } // passed from server for use in service calls on client
        public string AuthorizationToken { get; set; } // passed from server for use in service calls on client
        public string RemoteIPAddress { get; set; } // passed from server as cannot be reliably retrieved on client
        public bool IsPrerendering { get; set; }
        public string Platform { get; set; } //updated by Maui to retrieve platform in client

        private dynamic _properties;
        public dynamic Properties => _properties ?? (_properties = new PropertyDictionary());


        public void AppendHeadContent(string content)
        {
            if (string.IsNullOrEmpty(Properties.HeadContent))
            {
                Properties.HeadContent = content;
            }
            else if (!Properties.HeadContent.Contains(content))
            {
                Properties.HeadContent += content;
            }
        }

        public void Clone(SiteState siteState)
        {
            Alias = siteState.Alias;
            AntiForgeryToken = siteState.AntiForgeryToken;
            AuthorizationToken = siteState.AuthorizationToken;
            RemoteIPAddress = siteState.RemoteIPAddress;
            IsPrerendering = siteState.IsPrerendering;
        }
    }
}
