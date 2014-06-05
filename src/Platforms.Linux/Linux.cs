﻿// <airvpn_source_header>
// This file is part of AirVPN Client software.
// Copyright (C)2014-2014 AirVPN (support@airvpn.org) / https://airvpn.org )
//
// AirVPN Client is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// AirVPN Client is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with AirVPN Client. If not, see <http://www.gnu.org/licenses/>.
// </airvpn_source_header>

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Xml;
using AirVPN.Core;
using Mono.Unix.Native;

namespace AirVPN.Platforms
{
    public class Linux : Platform
    {
		private string m_architecture = "";

		// Override
		public Linux()
		{
 			m_architecture = NormalizeArchitecture(ShellPlatformIndipendent("sh", "-c 'uname -m'", "", true, false).Trim());
		}

		public override string GetCode()
		{
			return "Linux";
		}

		public override string GetArchitecture()
		{
			return m_architecture;
		}

        public override bool IsAdmin()
        {
            return (Environment.UserName == "root");
        }

		public override bool IsUnixSystem()
		{
			return true;
		}

		public override string VersionDescription()
        {
			string o = base.VersionDescription();
            o += " - " + ShellCmd("uname -a").Trim();
            return o;
        }

        public override void OpenUrl(string url)
        {
            System.Diagnostics.Process.Start("xdg-open", url);
        }

        public override string DirSep
        {
            get
            {
                return "/";
            }
        }

		public override string GetExecutablePath()
		{
			// We use this because querying .Net Assembly (what the base class do) doesn't work within Mkbundle.

			string output = "";
			StringBuilder builder = new StringBuilder(8192);
			if (Syscall.readlink("/proc/self/exe", builder) >= 0)
				output = builder.ToString();

			if ((output != "") && (new FileInfo(output).Name.ToLowerInvariant().StartsWith("mono")))
			{
				// Exception: Assembly directly load by Mono
				output = base.GetExecutablePath();
			}

			return output;
		}

        public override string GetUserFolder()
        {
            return Environment.GetEnvironmentVariable("HOME") + DirSep + ".airvpn";
        }

        public override string ShellCmd(string Command)
        {
            return Shell("sh", String.Format("-c '{0}'", Command), true);
        }

        public override void FlushDNS()
        {
            ShellCmd("/etc/rc.d/init.d/nscd restart");
        }


		public override void RouteAdd(string Address, string Mask, string Gateway)
		{
			string cmd = "/sbin/route add -net " + Address + " netmask " + Mask + " gw " + Gateway;
			ShellCmd(cmd);
		}

		public override void RouteRemove(string Address, string Mask, string Gateway)
		{
			string cmd = "/sbin/route del -net " + Address + " netmask " + Mask;
			ShellCmd(cmd);
		}
		
		public override string RouteList()
		{
			//string cmd = "route -v -n -e";
			string cmd = "netstat -nr";
			return ShellCmd(cmd);
		}

		public override string GenerateSystemReport()
		{
			string t = base.GenerateSystemReport();

			
			return t;
		}

		public override void OnAppStart()
		{
			base.OnAppStart();

			string dnsScriptPath = Software.FindResource("update-resolv-conf");
			if (dnsScriptPath == "")
			{
				Engine.Instance.Log(Engine.LogType.Error, "update-resolv-conf " + Messages.NotFound);
			}
		}

		public override void OnBuildOvpn(ref string ovpn)
		{
			base.OnBuildOvpn(ref ovpn);

			if (GetDnsSwitchMode() == "resolvconf")
			{
				string dnsScriptPath = Software.FindResource("update-resolv-conf");
				if (dnsScriptPath != "")
				{
					Engine.Instance.Log(Engine.LogType.Info, Messages.DnsResolvConfScript);
					ovpn += "script-security 2\n";
					ovpn += "up " + dnsScriptPath + "\n";
					ovpn += "down " + dnsScriptPath + "\n";
				}
			}			
		}

		public override void OnRecovery()
		{
			base.OnRecovery();

			OnDnsSwitchRestore();
		}

		public override void OnRecoveryLoad(XmlElement root)
		{
			base.OnRecoveryLoad(root);
		}

		public override void OnDnsSwitchDo(string dns)
		{
			base.OnDnsSwitchDo(dns);

			if (GetDnsSwitchMode() == "rename")
			{
				if (File.Exists("/etc/resolv.conf.airvpn") == false)
				{
					Engine.Instance.Log(Engine.LogType.Info, Messages.DnsRenameBackup);
					File.Copy("/etc/resolv.conf", "/etc/resolv.conf.airvpn");
				}

				Engine.Instance.Log(Engine.LogType.Info, Messages.DnsRenameDone);
				File.WriteAllText("/etc/resolv.conf", Messages.Format(Messages.ResolvConfHeader,Storage.GetVersionDesc()) + "\n\nnameserver " + dns + "\n");
			}			
		}

		public override void OnDnsSwitchRestore()
		{
			base.OnDnsSwitchRestore();

			// Cleaning rename method if pending
			if (File.Exists("/etc/resolv.conf.airvpn") == true)
			{
				Engine.Instance.Log(Engine.LogType.Info, Messages.DnsRenameRestored);

				File.Copy("/etc/resolv.conf.airvpn", "/etc/resolv.conf", true);
				File.Delete("/etc/resolv.conf.airvpn");
			}
		}

		public override string GetDriverAvailable()
		{
			string result = ShellCmd("cat /dev/net/tun");
			if (result.IndexOf("descriptor in bad state") != -1)
				return "Found";

			return "";
		}

		public override bool CanUnInstallDriver()
		{
			return false;
		}

		public override void InstallDriver()
		{			
		}

		public override void UnInstallDriver()
		{
		}





		public string GetDnsSwitchMode()
		{
			string current = Engine.Instance.Storage.Get("advanced.dns.mode").ToLowerInvariant();

			if (current == "auto")
			{
				if (File.Exists("/sbin/resolvconf"))
					current = "resolvconf";
				else
					current = "rename";
			}
			
			// Fallback
			if( (current == "resolvconv") && (Software.FindResource("update-resolv-conf") == "") )
				current = "rename";

			if ((current == "resolvconv") && (File.Exists("/sbin/resolvconf") == false))
				current = "rename";


			return current;			
		}
    }
}
