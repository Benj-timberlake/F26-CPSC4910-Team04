#!/usr/bin/env bash
# CloudFront in front of the beanstalk environment, for https without a domain. run once:
#   infra/cloudfront.sh BEANSTALK-04-env.eba-xxxx.us-east-1.elasticbeanstalk.com
set -euo pipefail

origin="${1:?usage: infra/cloudfront.sh <beanstalk environment hostname>}"

# aws managed policies: CachingDisabled and AllViewerAndCloudFrontHeaders-2022-06
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
