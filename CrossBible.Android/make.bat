rem Builds the Android packages of CrossBible (Release, native AOT) into the folder publish\:
rem
rem   CrossBible-<version>-android-arm64.apk   64 bit ARM phones (nearly all phones of the last years)
rem   CrossBible-<version>-android-arm.apk     32 bit ARM (older phones)
rem   CrossBible-<version>-android-x64.apk     x86_64 (emulators, Chromebooks)
rem   CrossBible-<version>.aab         the app bundle for the Google Play Store: contains all three, Play delivers the fitting one
rem   upload.keystore, signing.cmd     the upload key and its passwords (created on the first run, never commit them, back them up)
rem
rem   make.bat           builds the three APKs and the bundle
rem   make.bat aab       builds only the bundle
rem   make.bat apk       builds only the APKs
rem
rem The packages are signed with the upload key in publish\upload.keystore. Play Store: upload the .aab in the Play Console (Release > Production);
rem with Play App Signing, Google signs the apps for the users with its own key and this one only proves that the upload comes from you.
rem The version is Version in Directory.Build.props, the version code (ApplicationVersion) must grow with every upload to the Play Store.

setlocal
set "HERE=%~dp0"
set "OUT=%HERE%publish"
set "PROJECT=%HERE%CrossBible.Android.csproj"

set "DOAPK=1"
set "DOAAB=1"
for %%o in (%*) do (
    if /i "%%o"=="aab" set "DOAPK="
    if /i "%%o"=="apk" set "DOAAB="
)

set "VERSION="
for /f "usebackq delims=" %%i in (`dotnet msbuild "%PROJECT%" -getProperty:Version`) do set "VERSION=%%i"
if not defined VERSION (
    echo The version could not be read from Directory.Build.props.
    exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

rem The upload key: created once.
if not exist "%OUT%\upload.keystore" call :createkey || exit /b 1
call "%OUT%\signing.cmd"

set SIGN=-p:AndroidKeyStore=true -p:AndroidSigningKeyStore="%OUT%\upload.keystore" -p:AndroidSigningKeyAlias=crossbible -p:AndroidSigningKeyPass=%CB_PASS% -p:AndroidSigningStorePass=%CB_PASS%

if defined DOAPK (
    call :apk arm64 android-arm64 || exit /b 1
    call :apk arm android-arm || exit /b 1
    call :apk x64 android-x64 || exit /b 1
)

if defined DOAAB (
    if exist "%OUT%\build-aab" rmdir /s /q "%OUT%\build-aab"
    dotnet build-server shutdown >nul 2>&1
    dotnet publish "%PROJECT%" -c Release -f net11.0-android -p:UseSharedCompilation=false -p:AndroidAbis=all -p:AndroidPackageFormat=aab %SIGN% -o "%OUT%\build-aab" || exit /b 1
    for %%f in ("%OUT%\build-aab\*-Signed.aab") do copy /y "%%f" "%OUT%\CrossBible-%VERSION%.aab" >nul
    rmdir /s /q "%OUT%\build-aab"
)

echo.
echo Done: %OUT%
dir /b "%OUT%"
exit /b 0

rem One APK for one architecture: %1 is the name in the file, %2 the runtime identifier.
:apk
if exist "%OUT%\build-%1" rmdir /s /q "%OUT%\build-%1"
dotnet build-server shutdown >nul 2>&1
dotnet publish "%PROJECT%" -c Release -f net11.0-android -p:UseSharedCompilation=false -p:AndroidAbis=%2 -p:AndroidPackageFormat=apk %SIGN% -o "%OUT%\build-%1" || exit /b 1
for %%f in ("%OUT%\build-%1\*-Signed.apk") do copy /y "%%f" "%OUT%\CrossBible-%VERSION%-android-%1.apk" >nul
rmdir /s /q "%OUT%\build-%1"
exit /b 0

rem Creates the upload key and publish\signing.cmd with its password.
:createkey
set "KEYTOOL="
for /d %%j in ("%ProgramFiles%\Android\openjdk\*" "%ProgramFiles(x86)%\Android\openjdk\*") do if exist "%%~j\bin\keytool.exe" set "KEYTOOL=%%~j\bin\keytool.exe"
if not defined KEYTOOL if defined JAVA_HOME set "KEYTOOL=%JAVA_HOME%\bin\keytool.exe"
if not defined KEYTOOL echo keytool.exe was not found: install the Android SDK with its JDK, or set JAVA_HOME.& exit /b 1
for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "[guid]::NewGuid().ToString('N')"`) do set "CB_PASS=%%p"
"%KEYTOOL%" -genkeypair -v -keystore "%OUT%\upload.keystore" -alias crossbible -keyalg RSA -keysize 4096 -validity 10000 -storepass "%CB_PASS%" -keypass "%CB_PASS%" -dname "CN=CrossBible, O=Estrellas de Esperanza" || exit /b 1
> "%OUT%\signing.cmd" echo set "CB_PASS=%CB_PASS%"
echo Created %OUT%\upload.keystore and signing.cmd: back both up, without the key no update of the app can be uploaded (Play Console can reset a lost upload key).
exit /b 0
