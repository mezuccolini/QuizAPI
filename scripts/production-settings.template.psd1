@{
    DeploymentRoot = 'C:\Deployment'

    SiteName = 'Default Web Site'
    AppPoolName = 'QuizAPI'
    HttpPort = 80
    HttpsPort = 443

    # Leave blank to auto-detect the host name from PublicBaseUrl
    HostName = ''

    # Leave blank to create and use a self-signed certificate for HTTPS
    CertificateThumbprint = ''
    CreateSelfSignedCertificate = $true

    SqlInstance = '.\SQLEXPRESS'
    DatabaseName = 'QuizAPI'

    # Leave blank to let the installer build a local SQL Express connection string
    ConnectionString = ''

    PublicBaseUrl = 'http://quizapi.local'

    # Replace with a long random secret before production use
    JwtKey = 'REPLACE-WITH-A-LONG-RANDOM-SECRET-AT-LEAST-32-CHARS'
    JwtIssuer = 'TheCertMaster'
    JwtAudience = 'TheCertMasterUsers'

    BootstrapAdminEmail = 'admin@quizapi.local'
    BootstrapAdminPassword = 'REPLACE-WITH-A-STRONG-ADMIN-PASSWORD'
    BootstrapAdminFirstName = 'Server'
    BootstrapAdminLastName = 'Admin'

    CorsOrigins = @(
        'http://quizapi.local',
        'https://quizapi.local'
    )

    AuthAttemptsPerMinute = 8
    GuestQuizLoadsPerMinute = 20
    AuthenticatedQuizLoadsPerMinute = 60

    EnableSwagger = $false
    EnableHttpsRedirection = $false
}
