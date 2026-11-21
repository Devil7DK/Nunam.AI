# Packs the tree build-packages.sh publishes; the scriptlets are the shell scripts shared with the .deb.
Name:           nunam-indexer
Version:        %{pkg_version}
Release:        1
Summary:        Indexes local files into the Nunam search index
License:        AGPL-3.0-or-later
BuildArch:      x86_64

# Automatic dependencies would come from every native library the self-contained .NET runtime ships;
# the ones that matter are listed instead.
AutoReqProv:    no
Requires:       glibc libgcc libstdc++ openssl-libs libicu zlib systemd
Recommends:     tesseract tesseract-langpack-eng /usr/bin/ffmpeg acl

%global debug_package %{nil}
%global source_date_epoch_from_changelog 0
# The .NET runtime and native libraries ship prebuilt; stripping them or checking rpaths only breaks them.
%global __os_install_post %{nil}

%description
Runs on a machine with files: scans and watches its folders, parses documents, images, audio and
video, and writes what it finds to the Nunam database on the homelab.

%install
cp -a %{tree}/. %{buildroot}/

%files
/usr/lib/nunam-indexer
/usr/lib/systemd/system/nunam-indexer.service
/usr/sbin/nunam-indexer-account
/usr/lib/sysusers.d/nunam-indexer.conf
%dir %attr(0750,root,root) /etc/nunam-indexer
%config(noreplace) %attr(0640,root,root) /etc/nunam-indexer/appsettings.Local.json

%post
%(cat %{scripts}/after-install.sh)

%preun
%(cat %{scripts}/before-remove.sh)

%postun
%(cat %{scripts}/after-remove.sh)
