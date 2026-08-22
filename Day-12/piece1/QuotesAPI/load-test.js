import http from 'k6/http';
import { check } from 'k6';

export const options = {
    scenarios: {
        baseline: {
            executor: 'constant-vus',
            vus: 10,
            duration: '30s',
        },
    },

    thresholds: {
        http_req_duration: ['p(50)<5000', 'p(99)<10000'],
    },
};

export default function () {
    const response = http.get(
        'http://localhost:5228/api/performance/author-quotes'
    );

    check(response, {
        'status is 200': (r) => r.status === 200,
    });
}