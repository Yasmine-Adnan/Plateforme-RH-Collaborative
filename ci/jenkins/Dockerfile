# Jenkins officiel (Java 21) + le client Docker pour construire des images
FROM jenkins/jenkins:lts-jdk21

USER root
# On récupère le client Docker et le plugin buildx depuis l'image officielle de Docker
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker:27-cli /usr/local/libexec/docker/cli-plugins/ /usr/local/libexec/docker/cli-plugins/