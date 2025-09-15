// k6 Stress Tests for ASP.NET Core MeteredMemoryCache Example
// These tests gradually increase load to identify breaking points and system limits
// Run with: k6 run k6-stress-tests.js

import { check, sleep } from "k6";
import {
  config,
  httpOptions,
  thresholds,
  scenarios,
  getHealth,
  getMetrics,
  getUser,
  getProduct,
  getProductsByCategory,
  searchProducts,
  getUsers,
  updateUser,
  getCacheStats,
  clearCache,
  testCacheHit,
  testCacheInvalidation,
  getRandomUserId,
  getRandomProductId,
  getRandomCategoryId,
  getRandomSearchQuery,
  getRandomCacheName,
} from "./k6-config.js";

export let options = {
  scenarios: {
    stress: scenarios.stress,
  },
  thresholds: {
    // More lenient thresholds for stress tests - we expect some degradation
    http_req_duration: ["p(95)<2000"],
    http_req_failed: ["rate<0.2"], // Allow up to 20% failure rate under stress
    response_time: ["p(95)<1000"],
    cache_response_time: ["p(95)<200"],
    api_response_time: ["p(95)<2000"],
    error_rate: ["rate<0.15"], // Allow up to 15% error rate under stress
    cache_hit_rate: ["rate>0.6"], // Cache hit rate may degrade under stress
    cache_miss_rate: ["rate<0.4"],
  },
};

export function setup() {
  console.log(
    "🚀 Starting k6 Stress Tests for ASP.NET Core MeteredMemoryCache",
  );
  console.log(`📍 Base URL: ${config.baseUrl}`);
  console.log("💪 Gradually increasing load from 1 to 20 VUs over 9 minutes");

  // Verify application is running
  const healthResponse = getHealth();
  if (healthResponse.status !== 200) {
    throw new Error(
      `Application health check failed: ${healthResponse.status}`,
    );
  }

  console.log("✅ Application is healthy and ready for stress testing");
  return { startTime: new Date().toISOString() };
}

export default function (data) {
  // Stress test with aggressive patterns to find breaking points

  // Reduce think time under stress - users behave more aggressively
  const thinkTime = Math.random() * 0.5 + 0.1; // 0.1-0.6 seconds

  // Mix of operations to stress different parts of the system
  const operationType = Math.random();

  if (operationType < 0.3) {
    // 30% - Rapid user lookups (stress user cache)
    const userId = getRandomUserId();
    const userResponse = getUser(userId);
    check(userResponse, {
      "stress user lookup": (r) => r.status === 200,
      "stress user response time": (r) => r.timings.duration < 2000,
    });

    // Immediate follow-up request to test cache under stress
    sleep(0.05);
    const userResponse2 = getUser(userId);
    check(userResponse2, {
      "stress user cache hit": (r) => r.status === 200,
      "stress user cache response time": (r) => r.timings.duration < 500,
    });
  } else if (operationType < 0.5) {
    // 20% - Rapid product lookups (stress product cache)
    const productId = getRandomProductId();
    const productResponse = getProduct(productId);
    check(productResponse, {
      "stress product lookup": (r) => r.status === 200,
      "stress product response time": (r) => r.timings.duration < 2000,
    });

    // Immediate follow-up request
    sleep(0.05);
    const productResponse2 = getProduct(productId);
    check(productResponse2, {
      "stress product cache hit": (r) => r.status === 200,
      "stress product cache response time": (r) => r.timings.duration < 500,
    });
  } else if (operationType < 0.7) {
    // 20% - Batch operations (stress batch processing)
    const batchUserIds = [
      getRandomUserId(),
      getRandomUserId(),
      getRandomUserId(),
      getRandomUserId(),
      getRandomUserId(),
    ];
    const batchResponse = getUsers(batchUserIds);
    check(batchResponse, {
      "stress batch user lookup": (r) => r.status === 200,
      "stress batch response time": (r) => r.timings.duration < 2000,
    });
  } else if (operationType < 0.85) {
    // 15% - Search operations (stress search cache)
    const searchQuery = getRandomSearchQuery();
    const searchResponse = searchProducts(searchQuery, 1, 20);
    check(searchResponse, {
      "stress search operation": (r) => r.status === 200,
      "stress search response time": (r) => r.timings.duration < 2000,
    });

    // Immediate follow-up search
    sleep(0.05);
    const searchResponse2 = searchProducts(searchQuery, 1, 20);
    check(searchResponse2, {
      "stress search cache hit": (r) => r.status === 200,
      "stress search cache response time": (r) => r.timings.duration < 1000,
    });
  } else if (operationType < 0.95) {
    // 10% - Update operations (stress cache invalidation)
    const userId = getRandomUserId();
    const updateData = {
      name: `Stress Test User ${userId} ${Date.now()}`,
      email: `stress${userId}${Date.now()}@example.com`,
    };
    const updateResponse = updateUser(userId, updateData);
    check(updateResponse, {
      "stress user update": (r) => r.status === 204,
      "stress update response time": (r) => r.timings.duration < 2000,
    });

    // Immediate read after update to test cache invalidation under stress
    sleep(0.05);
    const userResponse = getUser(userId);
    check(userResponse, {
      "stress post-update read": (r) => r.status === 200,
      "stress post-update response time": (r) => r.timings.duration < 2000,
    });
  } else {
    // 5% - Cache management operations (stress cache system)
    const cacheOperation = Math.random();

    if (cacheOperation < 0.5) {
      // Cache statistics under stress
      const cacheStatsResponse = getCacheStats();
      check(cacheStatsResponse, {
        "stress cache stats": (r) => r.status === 200,
        "stress cache stats response time": (r) => r.timings.duration < 1000,
      });
    } else {
      // Cache clearing under stress
      const cacheName = getRandomCacheName();
      const clearResponse = clearCache(cacheName);
      check(clearResponse, {
        "stress cache clear": (r) => r.status === 204,
        "stress cache clear response time": (r) => r.timings.duration < 1000,
      });
    }
  }

  // Aggressive cache testing under stress (20% of iterations)
  if (Math.random() < 0.2) {
    const userId = getRandomUserId();
    const cacheTestResult = testCacheHit(userId);
    check(cacheTestResult, {
      "stress cache hit test": (r) =>
        r.miss.status === 200 && r.hit.status === 200,
      "stress cache performance": (r) => r.cacheHitFaster,
    });
  }

  // Rapid cache invalidation testing (10% of iterations)
  if (Math.random() < 0.1) {
    const userId = getRandomUserId();
    const invalidationResult = testCacheInvalidation(userId);
    check(invalidationResult, {
      "stress cache invalidation": (r) =>
        r.before.status === 200 &&
        r.update.status === 204 &&
        r.after.status === 200,
    });
  }

  // Metrics monitoring under stress (15% of iterations)
  if (Math.random() < 0.15) {
    const metricsResponse = getMetrics();
    check(metricsResponse, {
      "stress metrics check": (r) => r.status === 200,
      "stress metrics response time": (r) => r.timings.duration < 1000,
    });
  }

  // Health check under stress (10% of iterations)
  if (Math.random() < 0.1) {
    const healthResponse = getHealth();
    check(healthResponse, {
      "stress health check": (r) => r.status === 200,
      "stress health response time": (r) => r.timings.duration < 500,
    });
  }

  // Minimal think time under stress
  sleep(thinkTime);

  // Occasionally perform burst operations (5% of iterations)
  if (Math.random() < 0.05) {
    // Burst of rapid requests to test system resilience
    for (let i = 0; i < 5; i++) {
      const userId = getRandomUserId();
      const userResponse = getUser(userId);
      check(userResponse, {
        "burst user request": (r) => r.status === 200,
      });
      sleep(0.01); // Very short delay between burst requests
    }
  }
}

export function teardown(data) {
  console.log("🏁 Stress tests completed");
  console.log(`⏱️ Test duration: ${new Date().toISOString()}`);

  // Final health check
  const finalHealthResponse = getHealth();
  if (finalHealthResponse.status === 200) {
    console.log("✅ Application is still healthy after stress tests");
  } else {
    console.log("❌ Application health check failed after stress tests");
  }

  // Final cache statistics
  const finalCacheStats = getCacheStats();
  if (finalCacheStats.status === 200) {
    console.log("📊 Final cache statistics retrieved successfully");
  }

  // Final metrics check
  const finalMetrics = getMetrics();
  if (finalMetrics.status === 200) {
    console.log("📈 Final metrics retrieved successfully");
  }
}
