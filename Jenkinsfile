pipeline {
    agent any

    options {
        skipDefaultCheckout(true)
        timestamps()
        buildDiscarder(logRotator(numToKeepStr: '10'))
    }

    triggers {
        pollSCM('H/2 * * * *')
    }

    environment {
        DOCKERHUB_USER = 'yasmineadnan'
        IMAGE_NAME     = "${DOCKERHUB_USER}/plateforme-rh"
        IMAGE_TAG      = "${BUILD_NUMBER}"
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
            }
        }

        stage('Build') {
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:9.0'
                    args '-v nuget-cache:/root/.nuget/packages'
                    reuseNode true
                }
            }
            steps {
                sh 'dotnet build PlateformeRHCollaborative.Web.sln -c Release'
            }
        }

        stage('Tests') {
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:9.0'
                    args '-v nuget-cache:/root/.nuget/packages'
                    reuseNode true
                }
            }
            steps {
                sh 'dotnet test PlateformeRHCollaborative.Tests/PlateformeRHCollaborative.Tests.csproj -c Release --no-build --logger "junit;LogFileName=junit.xml" --results-directory TestResults'
            }
            post {
                always {
                    junit testResults: 'TestResults/*.xml', allowEmptyResults: false
                }
            }
        }

        stage('Image Docker') {
            steps {
                sh 'docker build -t $IMAGE_NAME:$IMAGE_TAG -t $IMAGE_NAME:latest .'
            }
        }

        stage('Push Docker Hub') {
            options {
                retry(3)
                timeout(time: 40, unit: 'MINUTES')
            }
            steps {
                withCredentials([usernamePassword(credentialsId: 'dockerhub',
                                                  usernameVariable: 'DH_USER',
                                                  passwordVariable: 'DH_TOKEN')]) {
                    sh 'echo "$DH_TOKEN" | docker login -u "$DH_USER" --password-stdin'
                    sh 'docker push $IMAGE_NAME:$IMAGE_TAG'
                    sh 'docker push $IMAGE_NAME:latest'
                }
            }
        }
    }

    post {
        always {
            sh 'docker logout || true'
        }
        success {
            echo "Image publiée : ${IMAGE_NAME}:${IMAGE_TAG}"
        }
        failure {
            echo "Le pipeline a échoué : rien n'a été publié."
        }
    }
}