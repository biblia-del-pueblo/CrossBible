Name: crossbible
Version: 1.0.0
Release: 1%{?dist}
Summary: This is Cross Bible, a cross platform bible reader.
License: MIT  
URL: https://www.crossbible.org
Requires: /bin/sh, sed, dotnet-runtime-11.0, libX11, libICE, libSM, fontconfig, mesa-libGL
AutoReqProv: no
BuildArch: noarch
Source0: %{name}-%{version}.tar.gz

%description
CrossBible is a cross platform bible reader. It runs on
Windows, Linux, macOS, Android and iOS. It can download a
lot of free bibles for many languages from ebible.org and
crosswire.org. It displays the bible text in columns for
easier reading, as your print bible does.

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
    sed -i 's|/usr/bin/crossbible|%{_bindir}/crossbible|g' /usr/share/applications/crossbible.desktop
#  %{_bindir}/crossbible
fi

%clean
rm -rf $RPM_BUILD_ROOT

%files
/usr/bin/crossbible
/usr/share/crossbible/*
/usr/share/pixmaps/crossbible.png
/usr/share/applications/crossbible.desktop
/usr/share/doc/crossbible/copyright
/usr/share/doc/crossbible/ChangeLog
/usr/share/doc/crossbible/README

%changelog
* Tue Nov 17 2026 Simon Egli <simon.jakob.egli@gmail.com> - 2.0.0
- First version being packaged
