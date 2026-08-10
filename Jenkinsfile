// Build/deploy pipeline for the Rise .NET app.
//
// Runs on the Jenkins controller itself (single-VM setup, no separate build
// agent), so it needs the .NET SDK installed there - see the buildserver
// Ansible role. APP_SERVER_HOST / APP_DEPLOY_USER / APP_NAME / APP_DIR /
// APP_PORT come from Jenkins' global node properties (set via JCasC), so
// this same file works unchanged against the cloud environment later - only
// the Ansible-side values change, not this pipeline.
pipeline {
    agent any

    options {
        timestamps()
    }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
        PUBLISH_DIR = 'publish'
    }

    stages {
        stage('Lint') {
            steps {
                sh 'dotnet format Rise.sln --verify-no-changes --severity warn'
            }
        }

        stage('Build') {
            steps {
                sh 'dotnet build Rise.sln --configuration Release'
            }
        }

        stage('Test') {
            steps {
                sh 'dotnet test Rise.sln --configuration Release --no-build --logger trx --results-directory TestResults'
            }
            post {
                always {
                    step([$class: 'MSTestPublisher', testResultsFile: '**/TestResults/*.trx', failOnError: true, keepLongStdio: true])
                }
            }
        }

        stage('Deploy') {
            steps {
                // Safety net: confirms the Blazor WASM client's (Rise.Client,
                // referenced by Rise.Server) workload packs are aligned for
                // this specific project. Needs root to write into
                // /usr/share/dotnet if anything is out of sync - see the
                // buildserver Ansible role for the narrow sudo grant.
                sh 'sudo dotnet workload restore src/Rise.Client/Rise.Client.csproj'
                // No --no-self-contained: it's redundant (framework-dependent
                // is already the default with no RuntimeIdentifier set) and
                // actively harmful - it propagates SelfContained=false down
                // into Rise.Client's own build, and the WASM SDK's runtime
                // pack resolution then comes back empty (WASM0005: Unable to
                // resolve WebAssembly runtime pack version), even though the
                // packs themselves are correctly installed. Confirmed by
                // testing the exact same publish with/without this flag.
                sh "dotnet publish src/Rise.Server/Rise.Server.csproj --configuration Release -o ${env.PUBLISH_DIR}"
                sshagent(["deploy-ssh-key"]) {
                    sh """
                        rsync -az --delete -e 'ssh -o StrictHostKeyChecking=no' ${env.PUBLISH_DIR}/ ${env.APP_DEPLOY_USER}@${env.APP_SERVER_HOST}:${env.APP_DIR}/
                        ssh -tt -o StrictHostKeyChecking=no ${env.APP_DEPLOY_USER}@${env.APP_SERVER_HOST} 'sudo systemctl restart ${env.APP_NAME}'
                    """
                }
            }
        }
    }

    post {
        always {
            cleanWs()
        }
    }
}
