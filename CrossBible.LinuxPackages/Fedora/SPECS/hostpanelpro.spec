Name: hostpanelpro
Version: 1.0.0
Release: 1%{?dist}
Summary: This is the HostPanelPro Installer
License: Creative Commons Share-alike    
URL: https://www.hostpanelpro.com
Requires: /bin/sh, sed, dotnet-runtime-10.0
AutoReqProv: no
BuildArch: noarch
Source0: %{name}-%{version}.tar.gz

%description
HostPanelPro is a complete management portal for Cloud Computing Companies
and IT Providers to automate the provisioning of a full suite of Multi-Tenant
services on servers. The powerful, flexible and fully open source HostPanelPro platform
gives users simple point-and-click control over Server applications including IIS 10,
Microsoft SQL Server 2022, MySQL, MariaDB, Active Directory, Microsoft Exchange 2025,
Microsoft Sharepoint 2019, Microsoft RemoteApp/RDS and Hyper-V.

%prep
%autosetup

%install
rm -rf $RPM_BUILD_ROOT
mkdir -p $RPM_BUILD_ROOT%{_bindir}
mkdir -p $RPM_BUILD_ROOT/usr/share
cp -rp usr/bin/* $RPM_BUILD_ROOT%{_bindir}
cp -rp usr/share/* $RPM_BUILD_ROOT/usr/share

%post
if [ $1 -ge 1 ];then
    sed -i 's|/usr/bin/hostpanelpro-installer|%{_bindir}/hostpanelpro-installer|g' /usr/share/applications/hostpanelpro-installer.desktop
#  %{_bindir}/hostpanelpro-installer
    rm /usr/share/hostpanelpro/Installer/Setup2.*
    echo "================================================================"
    echo "Please run 'sudo hostpanelpro-installer' to install HostPanelPro"
    echo "================================================================"
fi

%clean
rm -rf $RPM_BUILD_ROOT

%files
/usr/bin/hostpanelpro-installer
/usr/bin/sslcertmaker
/usr/share/hostpanelpro/Installer/*
/usr/share/pixmaps/HostPanelPro.png
/usr/share/pixmaps/sslcertmaker.png
/usr/share/applications/hostpanelpro-installer.desktop
/usr/share/applications/sslcertmaker.desktop
/usr/share/doc/hostpanelpro/copyright
/usr/share/doc/hostpanelpro/ChangeLog
/usr/share/doc/hostpanelpro/README
/usr/share/man/man1/hostpanelpro-installer.1.gz

%changelog
* Fri Mar 22 2024 Simon Egli <simon.jakob.egli@gmail.com> - 2.0.0
- First version being packaged
