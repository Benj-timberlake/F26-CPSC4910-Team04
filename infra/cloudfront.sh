#!/usr/bin/env bash
# Puts a CloudFront distribution in front of the Elastic Beanstalk environment so the site has
# HTTPS on a *.cloudfront.net address without buying a domain. Run once, by hand, with AWS
# credentials that can manage CloudFront:
#
#   infra/cloudfront.sh BEANSTALK-04-env.eba-xxxx.us-east-1.elasticbeanstalk.com
#
# Prints the https address when done. Afterwards set FRONTEND_URL on the backend and register
# https://<that address>/signin-google and /signin-microsoft with the sso providers.
set -euo pipefail

origin="${1:?usage: infra/cloudfront.sh <beanstalk environment hostname>}"

# aws managed policies: no caching (this is an app, not a static site), and forward every viewer
# header, cookie and query string plus CloudFront-Forwarded-Proto so the app knows it was https
caching_disabled="4135ea2d-6df8-44a3-9df3-4b5a84be39ad"
all_viewer_and_cloudfront="33f36d7e-f396-46d9-90e0-52428a34d9dc"

config=$(cat <<JSON
{
  "CallerReference": "truckerreward-$(date +%s)",
  "Comment": "TruckerReward: https in front of elastic beanstalk",
  "Enabled": true,
  "HttpVersion": "http2",
  "PriceClass": "PriceClass_100",
  "Origins": {
    "Quantity": 1,
    "Items": [{
      "Id": "beanstalk",
      "DomainName": "$origin",
      "CustomOriginConfig": {
        "HTTPPort": 80,
        "HTTPSPort": 443,
        "OriginProtocolPolicy": "http-only",
        "OriginReadTimeout": 60,
        "OriginKeepaliveTimeout": 60
      }
    }]
  },
  "DefaultCacheBehavior": {
    "TargetOriginId": "beanstalk",
    "ViewerProtocolPolicy": "redirect-to-https",
    "AllowedMethods": {
      "Quantity": 7,
      "Items": ["GET", "HEAD", "OPTIONS", "PUT", "PATCH", "POST", "DELETE"],
      "CachedMethods": { "Quantity": 2, "Items": ["GET", "HEAD"] }
    },
    "Compress": true,
    "CachePolicyId": "$caching_disabled",
    "OriginRequestPolicyId": "$all_viewer_and_cloudfront"
  }
}
JSON
)

domain=$(aws cloudfront create-distribution --distribution-config "$config" --query 'Distribution.DomainName' --output text)
echo "created, it takes a few minutes to deploy: https://$domain"
