-- run once against Team04_DB before deploying this branch

-- goals drivers earn badges for. a null sponsor_company_id is a built-in goal for every driver,
-- otherwise only that company's drivers see it and only that company's points count
CREATE TABLE IF NOT EXISTS achievements (
    id INT NOT NULL AUTO_INCREMENT,
    sponsor_company_id INT NULL,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(255) NOT NULL,
    -- points_earned, points_spent or orders_placed
    metric VARCHAR(30) NOT NULL,
    threshold INT NOT NULL,
    created_by INT NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    KEY ix_achievements_company (sponsor_company_id, active),
    CONSTRAINT fk_achievements_company FOREIGN KEY (sponsor_company_id) REFERENCES companies (id) ON DELETE CASCADE,
    CONSTRAINT fk_achievements_created_by FOREIGN KEY (created_by) REFERENCES users (id) ON DELETE SET NULL
);

-- one row per badge a driver has earned
CREATE TABLE IF NOT EXISTS earned_badges (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    achievement_id INT NOT NULL,
    earned_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_earned_badges_user_achievement (user_id, achievement_id),
    KEY ix_earned_badges_achievement (achievement_id),
    CONSTRAINT fk_earned_badges_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_earned_badges_achievement FOREIGN KEY (achievement_id) REFERENCES achievements (id) ON DELETE CASCADE
);

-- the built-in goals, same ids as Achievement.BuiltIn
INSERT IGNORE INTO achievements (id, sponsor_company_id, name, description, metric, threshold, created_by, active) VALUES
    (1, NULL, 'First points', 'Earn your first points.', 'points_earned', 1, NULL, 1),
    (2, NULL, '1,000 points', 'Earn 1,000 points in all.', 'points_earned', 1000, NULL, 1),
    (3, NULL, '5,000 points', 'Earn 5,000 points in all.', 'points_earned', 5000, NULL, 1),
    (4, NULL, '10,000 points', 'Earn 10,000 points in all.', 'points_earned', 10000, NULL, 1),
    (5, NULL, 'First order', 'Place your first order.', 'orders_placed', 1, NULL, 1),
    (6, NULL, 'Regular shopper', 'Place 5 orders.', 'orders_placed', 5, NULL, 1),
    (7, NULL, 'Big spender', 'Spend 5,000 points on orders.', 'points_spent', 5000, NULL, 1);
