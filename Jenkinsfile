pipeline {
  agent any
  stages {
    stage('Build') {
      agent {
        node {
          label 'macOS'
        }
        
      }
      steps {
        sh 'dotnet build'
      }
    }
  }
}