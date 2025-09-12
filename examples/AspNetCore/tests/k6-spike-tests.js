// k6 Spike Tests for ASP.NET Core MeteredMemoryCache Example
// These tests simulate sudden traffic spikes to test system resilience and recovery
// Run with: k6 run k6-spike-tests.js

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
    spike: scenarios.spike,
  },
  thresholds: {
    // Moderate thresholds for spike tests - expect some degradation during spikes
    http_req_duration: ["p(95)<1500"],
    http_req_failed: ["rate<0.15"], // Allow up to 15% failure rate during spikes
    response_time: ["p(95)<800"],
    cache_response_time: ["p(95)<150"],
    api_response_time: ["p(95)<1500"],
    error_rate: ["rate<0.1"], // Allow up to 10% error rate during spikes
    cache_hit_rate: ["rate>0.7"], // Cache hit rate may degrade during spikes
    cache_miss_rate: ["rate<0.3"],
  },
};

export function setup() {
  console.log("🚀 Starting k6 Spike Tests for ASP.NET Core MeteredMemoryCache");
  console.log(`📍 Base URL: ${config.baseUrl}`);
  console.log("⚡ Simulating traffic spikes: 10 → 50 → 10 VUs over 4 minutes");

  // Verify application is running
  const healthResponse = getHealth();
  if (healthResponse.status !== 200) {
    throw new Error(
      `Application health check failed: ${healthResponse.status}`,
    );
  }

  console.log("✅ Application is healthy and ready for spike testing");
  return { startTime: new Date().toISOString() };
}

export default function (data) {
  // Spike test with varying load patterns

  // Determine current load phase based on test duration
  const currentTime = new Date().getTime();
  const testStartTime = new Date(data.startTime).getTime();
  const elapsedMinutes = (currentTime - testStartTime) / (1000 * 60);

  let isSpikePhase = false;
  let isRecoveryPhase = false;

  if (elapsedMinutes >= 1 && elapsedMinutes < 2) {
    isSpikePhase = true; // 50 VUs - peak load
  } else if (elapsedMinutes >= 2 && elapsedMinutes < 3) {
    isRecoveryPhase = true; // 10 VUs - recovery phase
  }

  // Adjust behavior based on load phase
  let thinkTime, operationIntensity;

  if (isSpikePhase) {
    // During spike: aggressive behavior, minimal think time
    thinkTime = Math.random() * 0.2 + 0.05; // 0.05-0.25 seconds
    operationIntensity = 0.8; // 80% chance of operation
  } else if (isRecoveryPhase) {
    // During recovery: moderate behavior
    thinkTime = Math.random() * 1 + 0.5; // 0.5-1.5 seconds
    operationIntensity = 0.6; // 60% chance of operation
  } else {
    // Normal phase: realistic behavior
    thinkTime = Math.random() * 2 + 1; // 1-3 seconds
    operationIntensity = 0.4; // 40% chance of operation
  }

  // Perform operations based on intensity
  if (Math.random() < operationIntensity) {
    const operationType = Math.random();

    if (operationType < 0.3) {
      // 30% - User operations (most common)
      const userId = getRandomUserId();
      const userResponse = getUser(userId);
      check(userResponse, {
        "spike user lookup": (r) => r.status === 200,
        "spike user response time": (r) => r.timings.duration < 1500,
      });

      // During spike phase, test cache behavior more aggressively
      if (isSpikePhase && Math.random() < 0.5) {
        sleep(0.01);
        const userResponse2 = getUser(userId);
        check(userResponse2, {
          "spike user cache hit": (r) => r.status === 200,
          "spike user cache response time": (r) => r.timings.duration < 500,
        });
      }
    } else if (operationType < 0.5) {
      // 20% - Product operations
      const productId = getRandomProductId();
      const productResponse = getProduct(productId);
      check(productResponse, {
        "spike product lookup": (r) => r.status === 200,
        "spike product response time": (r) => r.timings.duration < 1500,
      });

      // During spike phase, test cache behavior
      if (isSpikePhase && Math.random() < 0.5) {
        sleep(0.01);
        const productResponse2 = getProduct(productId);
        check(productResponse2, {
          "spike product cache hit": (r) => r.status === 200,
          "spike product cache response time": (r) => r.timings.duration < 500,
        });
      }
    } else if (operationType < 0.7) {
      // 20% - Search operations
      const searchQuery = getRandomSearchQuery();
      const searchResponse = searchProducts(searchQuery, 1, 10);
      check(searchResponse, {
        "spike search operation": (r) => r.status === 200,
        "spike search response time": (r) => r.timings.duration < 1500,
      });
    } else if (operationType < 0.85) {
      // 15% - Batch operations
      const batchUserIds = [
        getRandomUserId(),
        getRandomUserId(),
        getRandomUserId(),
      ];
      const batchResponse = getUsers(batchUserIds);
      check(batchResponse, {
        "spike batch user lookup": (r) => r.status === 200,
        "spike batch response time": (r) => r.timings.duration < 1500,
      });
    } else if (operationType < 0.95) {
      // 10% - Update operations
      const userId = getRandomUserId();
      const updateData = {
        name: `Spike Test User ${userId} ${Date.now()}`,
        email: `spike${userId}${Date.now()}@example.com`,
      };
      const updateResponse = updateUser(userId, updateData);
      check(updateResponse, {
        "spike user update": (r) => r.status === 204,
        "spike update response time": (r) => r.timings.duration < 1500,
      });
    } else {
      // 5% - Cache management operations
      const cacheOperation = Math.random();

      if (cacheOperation < 0.5) {
        const cacheStatsResponse = getCacheStats();
        check(cacheStatsResponse, {
          "spike cache stats": (r) => r.status === 200,
          "spike cache stats response time": (r) => r.timings.duration < 1000,
        });
      } else {
        const cacheName = getRandomCacheName();
        const clearResponse = clearCache(cacheName);
        check(clearResponse, {
          "spike cache clear": (r) => r.status === 204,
          "spike cache clear response time": (r) => r.timings.duration < 1000,
        });
      }
    }
  }

  // During spike phase, perform additional stress operations
  if (isSpikePhase) {
    // Burst operations during spike (30% chance)
    if (Math.random() < 0.3) {
      const burstOperations = Math.floor(Math.random() * 5) + 3; // 3-7 operations

      for (let i = 0; i < burstOperations; i++) {
        const userId = getRandomUserId();
        const userResponse = getUser(userId);
        check(userResponse, {
          "spike burst user request": (r) => r.status === 200,
        });
        sleep(0.01); // Very short delay between burst requests
      }
    }

    // Rapid cache testing during spike (20% chance)
    if (Math.random() < 0.2) {
      const userId = getRandomUserId();
      const cacheTestResult = testCacheHit(userId);
      check(cacheTestResult, {
        "spike cache hit test": (r) =>
          r.miss.status === 200 && r.hit.status === 200,
        "spike cache performance": (r) => r.cacheHitFaster,
      });
    }

    // Rapid cache invalidation during spike (10% chance)
    if (Math.random() < 0.1) {
      const userId = getRandomUserId();
      const invalidationResult = testCacheInvalidation(userId);
      check(invalidationResult, {
        "spike cache invalidation": (r) =>
          r.before.status === 200 &&
          r.update.status === 204 &&
          r.after.status === 200,
      });
    }
  }

  // During recovery phase, monitor system recovery
  if (isRecoveryPhase) {
    // More frequent health checks during recovery (20% chance)
    if (Math.random() < 0.2) {
      const healthResponse = getHealth();
      check(healthResponse, {
        "spike recovery health check": (r) => r.status === 200,
        "spike recovery health response time": (r) => r.timings.duration < 500,
      });
    }

    // More frequent metrics checks during recovery (15% chance)
    if (Math.random() < 0.15) {
      const metricsResponse = getMetrics();
      check(metricsResponse, {
        "spike recovery metrics check": (r) => r.status === 200,
        "spike recovery metrics response time": (r) =>
          r.timings.duration < 1000,
      });
    }
  }

  // Normal monitoring operations (5% chance)
  if (Math.random() < 0.05) {
    const healthResponse = getHealth();
    check(healthResponse, {
      "spike health check": (r) => r.status === 200,
      "spike health response time": (r) => r.timings.duration < 500,
    });
  }

  // Metrics monitoring (3% chance)
  if (Math.random() < 0.03) {
    const metricsResponse = getMetrics();
    check(metricsResponse, {
      "spike metrics check": (r) => r.status === 200,
      "spike metrics response time": (r) => r.timings.duration < 1000,
    });
  }

  // Think time based on current phase
  sleep(thinkTime);

  // Simulate user session patterns during different phases
  if (Math.random() < 0.1) {
    const sessionUserId = getRandomUserId();

    // User session: profile → search → product view
    const userResponse = getUser(sessionUserId);
    check(userResponse, {
      "spike session user lookup": (r) => r.status === 200,
    });

    sleep(isSpikePhase ? 0.1 : 0.5);

    const searchQuery = getRandomSearchQuery();
    const searchResponse = searchProducts(searchQuery, 1, 10);
    check(searchResponse, {
      "spike session search": (r) => r.status === 200,
    });

    sleep(isSpikePhase ? 0.1 : 1);

    const productId = getRandomProductId();
    const productResponse = getProduct(productId);
    check(productResponse, {
      "spike session product view": (r) => r.status === 200,
    });
  }
}

export function teardown(data) {
  console.log("🏁 Spike tests completed");
  console.log(`⏱️ Test duration: ${new Date().toISOString()}`);

  // Final health check
  const finalHealthResponse = getHealth();
  if (finalHealthResponse.status === 200) {
    console.log("✅ Application is still healthy after spike tests");
  } else {
    console.log("❌ Application health check failed after spike tests");
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

  console.log("🔍 Spike test analysis recommendations:");
  console.log("   - Check response time patterns during spike phases");
  console.log("   - Monitor error rates during traffic spikes");
  console.log("   - Verify system recovery after spike ends");
  console.log("   - Review cache performance under load");
  console.log("   - Check for any memory or resource leaks");
}
