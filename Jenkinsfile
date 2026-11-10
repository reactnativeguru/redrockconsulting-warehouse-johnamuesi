// ---------------------------------------------------------------------------
// Declarative Jenkins pipeline, GitFlow-aware.
//
//   feature/*  -> build + test only (fast feedback on the PR)
//   develop    -> build + test + push images + deploy to DEV (k8s)
//   release/*  -> ... + deploy to STAGING
//   main       -> ... + manual approval + deploy to PRODUCTION
//   hotfix/*   -> build + test (merged to main and develop after review)
//
// Each stage runs in a throwaway Docker container, so build agents don't need
// .NET/Node installed and every build uses identical tool versions.
// ---------------------------------------------------------------------------
pipeline {
  agent none

  options {
    timestamps()
    timeout(time: 45, unit: 'MINUTES')
    buildDiscarder(logRotator(numToKeepStr: '30'))
  }

  environment {
    REGISTRY  = 'basecampacr.azurecr.io'            // Azure Container Registry
    IMAGE_TAG = "${env.BRANCH_NAME.replaceAll('/', '-')}-${env.BUILD_NUMBER}-${env.GIT_COMMIT?.take(7) ?: 'local'}"
  }

  stages {
    stage('Test') {
      // Independent test suites run in parallel - faster feedback.
      parallel {
        stage('Backend (.NET)') {
          agent { docker { image 'mcr.microsoft.com/dotnet/sdk:10.0'; reuseNode true } }
          steps {
            dir('backend') {
              sh 'dotnet restore BasecampRetail.slnx'
              sh 'dotnet build BasecampRetail.slnx -c Release --no-restore'
              sh 'dotnet test BasecampRetail.slnx -c Release --no-build --logger "trx;LogFileName=results.trx" --collect:"XPlat Code Coverage"'
            }
          }
          post { always { mstest testResultsFile: 'backend/**/TestResults/*.trx', failOnError: false } }
        }

        stage('Handheld app (React Native)') {
          agent { docker { image 'node:22'; reuseNode true } }
          steps {
            dir('mobile') {
              sh 'npm ci'
              sh 'npm run typecheck'
              sh 'npm run test:ci'
            }
          }
        }

        stage('Admin portal (micro-frontends)') {
          agent { docker { image 'node:22'; reuseNode true } }
          steps {
            dir('web') {
              sh 'npm ci'
              sh 'npm run typecheck'
              sh 'CI=true npm test'
              sh 'npm run build'
            }
          }
        }
      }
    }

    stage('Build & push images') {
      when { anyOf { branch 'develop'; branch 'main'; branch pattern: 'release/.*', comparator: 'REGEXP' } }
      agent { label 'docker' }
      steps {
        withCredentials([usernamePassword(credentialsId: 'acr-push', usernameVariable: 'ACR_USER', passwordVariable: 'ACR_PASS')]) {
          sh 'echo "$ACR_PASS" | docker login $REGISTRY -u "$ACR_USER" --password-stdin'
          script {
            def services = [
              catalog   : 'src/Services/Catalog/Catalog.Api/Catalog.Api.csproj',
              inventory : 'src/Services/Inventory/Inventory.Api/Inventory.Api.csproj',
              orders    : 'src/Services/Orders/Orders.Api/Orders.Api.csproj',
              'epos-etl': 'src/Services/Etl/Epos.Etl/Epos.Etl.csproj',
              gateway   : 'src/Gateway/Gateway/Gateway.csproj',
            ]
            services.each { name, project ->
              def entry = project.tokenize('/').last().replace('.csproj', '.dll')
              sh """
                docker build backend \
                  --build-arg PROJECT=${project} --build-arg ENTRY=${entry} \
                  -t ${REGISTRY}/${name}:${IMAGE_TAG}
                docker push ${REGISTRY}/${name}:${IMAGE_TAG}
              """
            }
            sh "docker build web -t ${REGISTRY}/admin-portal:${IMAGE_TAG} && docker push ${REGISTRY}/admin-portal:${IMAGE_TAG}"
          }
        }
      }
    }

    stage('Deploy DEV') {
      when { branch 'develop' }
      agent { label 'docker' }
      steps { deployTo('dev') }
    }

    stage('Deploy STAGING') {
      when { branch pattern: 'release/.*', comparator: 'REGEXP' }
      agent { label 'docker' }
      steps { deployTo('staging') }
    }

    stage('Approve PRODUCTION') {
      when { branch 'main' }
      steps {
        timeout(time: 1, unit: 'DAYS') {
          input message: "Deploy ${IMAGE_TAG} to production?", ok: 'Deploy'
        }
      }
    }

    stage('Deploy PRODUCTION') {
      when { branch 'main' }
      agent { label 'docker' }
      steps { deployTo('prod') }
    }
  }

  post {
    failure { echo "Build failed - notify the team channel here (Teams/Slack plugin)." }
  }
}

// Pin every image to this build's tag in the kustomize overlay, then apply.
// `kubectl rollout status` fails the build if pods don't become ready.
def deployTo(String envName) {
  withKubeConfig([credentialsId: "kubeconfig-${envName}"]) {
    dir("infra/k8s/overlays/${envName}") {
      ['catalog', 'inventory', 'orders', 'epos-etl', 'gateway', 'admin-portal'].each { svc ->
        sh "kustomize edit set image basecamp/${svc}=${env.REGISTRY}/${svc}:${env.IMAGE_TAG}"
      }
      sh 'kubectl apply -k .'
      ['catalog', 'inventory', 'orders', 'gateway', 'admin-portal'].each { svc ->
        sh "kubectl -n basecamp rollout status deployment/${svc} --timeout=180s"
      }
    }
  }
}
