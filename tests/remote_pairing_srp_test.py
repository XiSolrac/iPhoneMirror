"""Verify real SRP proofs using the representation sent by the pinned SDK."""
import hashlib
from pathlib import Path
import sys
import unittest

from srptools import SRPContext, SRPServerSession
from srptools.constants import PRIME_3072, PRIME_3072_GEN

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import usb_touch_bridge as bridge


class RemotePairingSrpTests(unittest.TestCase):
    def setUp(self):
        parameters = dict(prime=PRIME_3072, generator=PRIME_3072_GEN,
                          hash_func=hashlib.sha512)
        context = SRPContext('Pair-Setup', password='000000', **parameters)
        username, verifier, salt = context.get_user_data_triplet()
        server = SRPServerSession(SRPContext(username, **parameters), password_verifier=verifier)
        self.client = bridge._ts.SRPClientSession(context)
        self.client.process(server.public, salt)
        server.process(self.client.public, salt)
        self.assertTrue(server.verify_proof(bytes.fromhex(self.client.key_proof)))
        self.proof = server.key_proof_hash

    def test_sdk_hexadecimal_bytes_verify(self):
        self.assertTrue(self.client.verify_proof(self.proof.encode('ascii')))

    def test_hexadecimal_text_still_verifies(self):
        self.assertTrue(self.client.verify_proof(self.proof))

    def test_modified_proof_is_rejected_in_both_representations(self):
        forged = ('00' if self.proof[:2] != '00' else '01') + self.proof[2:]
        for proof in (forged, forged.encode('ascii')):
            with self.subTest(proof_type=type(proof).__name__):
                self.assertFalse(self.client.verify_proof(proof))


if __name__ == '__main__':
    unittest.main()
