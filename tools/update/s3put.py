#!/usr/bin/env python3
"""Uploads files to an S3-compatible bucket (ArvanCloud Object Storage) with AWS Signature V4. Standard library only.
Keys come from the environment and are never printed or stored:
  ARVAN_ACCESS_KEY, ARVAN_SECRET_KEY
Settings (defaults match the Shabpa bucket):
  ARVAN_BUCKET   (shabpa-dl)    ARVAN_ENDPOINT (s3.ir-central1.arvanstorage.ir)    ARVAN_REGION (ir-central1)
usage: s3put.py <prefix> <file> [<file> ...]      e.g.  s3put.py update/ installer/ShabpaSetup.exe installer/latest.json
       s3put.py --selftest                        checks the signing code against the AWS documentation example
Files are uploaded in the order given, public-read, and verified afterwards by downloading them again over HTTPS."""
import datetime, hashlib, hmac, os, sys, urllib.request, urllib.error, urllib.parse


def _h(key, msg): return hmac.new(key, msg.encode(), hashlib.sha256).digest()


def signature(secret, date, region, service, string_to_sign):
    k = _h(_h(_h(_h(("AWS4" + secret).encode(), date), region), service), "aws4_request")
    return hmac.new(k, string_to_sign.encode(), hashlib.sha256).hexdigest()


def sign_request(method, host, path, query, headers, payload_hash, amzdate, region, access, secret):
    """Returns the Authorization header value. headers must contain every header that is signed (lower-case names)."""
    names = sorted(headers)
    canon_headers = "".join(f"{n}:{headers[n].strip()}\n" for n in names)
    signed = ";".join(names)
    canonical = "\n".join([method, path, query, canon_headers, signed, payload_hash])
    date = amzdate[:8]
    scope = f"{date}/{region}/s3/aws4_request"
    sts = "\n".join(["AWS4-HMAC-SHA256", amzdate, scope, hashlib.sha256(canonical.encode()).hexdigest()])
    sig = signature(secret, date, region, "s3", sts)
    return f"AWS4-HMAC-SHA256 Credential={access}/{scope}, SignedHeaders={signed}, Signature={sig}"


def selftest():
    # the GET Object example from the AWS Signature V4 documentation
    auth = sign_request("GET", "examplebucket.s3.amazonaws.com", "/test.txt", "",
                        {"host": "examplebucket.s3.amazonaws.com", "range": "bytes=0-9",
                         "x-amz-content-sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                         "x-amz-date": "20130524T000000Z"},
                        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "20130524T000000Z", "us-east-1",
                        "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")
    expect = "f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41"
    assert auth.endswith("Signature=" + expect), auth
    print("selftest ok")


def put(bucket, endpoint, region, access, secret, key, data):
    host = f"{bucket}.{endpoint}"
    path = "/" + urllib.parse.quote(key, safe="/-_.~")
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    payload = hashlib.sha256(data).hexdigest()
    hdr = {"host": host, "x-amz-acl": "public-read", "x-amz-content-sha256": payload, "x-amz-date": now}
    auth = sign_request("PUT", host, path, "", hdr, payload, now, region, access, secret)
    req = urllib.request.Request(f"https://{host}{path}", data=data, method="PUT",
                                 headers={"x-amz-acl": "public-read", "x-amz-content-sha256": payload, "x-amz-date": now,
                                          "Authorization": auth, "Content-Length": str(len(data))})
    with urllib.request.urlopen(req, timeout=300) as r:
        return r.status


def main(argv):
    if argv[:1] == ["--selftest"]: return selftest()
    if len(argv) < 2: sys.exit(__doc__)
    access, secret = os.environ.get("ARVAN_ACCESS_KEY"), os.environ.get("ARVAN_SECRET_KEY")
    if not access or not secret: sys.exit("set ARVAN_ACCESS_KEY and ARVAN_SECRET_KEY first (see docs/UPDATES.md)")
    bucket = os.environ.get("ARVAN_BUCKET", "shabpa-dl")
    endpoint = os.environ.get("ARVAN_ENDPOINT", "s3.ir-central1.arvanstorage.ir")
    region = os.environ.get("ARVAN_REGION", "ir-central1")
    prefix = argv[0]
    for f in argv[1:]:
        data = open(f, "rb").read()
        key = prefix + os.path.basename(f)
        try:
            status = put(bucket, endpoint, region, access, secret, key, data)
        except urllib.error.HTTPError as e:
            sys.exit(f"upload of {key} failed: HTTP {e.code} {e.read()[:300]!r}")
        # read it back over plain public HTTPS and compare, so a half-upload can never be announced
        back = urllib.request.urlopen(f"https://{bucket}.{endpoint}/{urllib.parse.quote(key)}", timeout=300).read()
        if hashlib.sha256(back).digest() != hashlib.sha256(data).digest():
            sys.exit(f"{key}: uploaded ({status}) but the public copy differs")
        print(f"ok {key} ({len(data)} bytes, verified publicly)")


if __name__ == "__main__":
    main(sys.argv[1:])
