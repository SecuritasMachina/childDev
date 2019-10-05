package tests;
import org.junit.AfterClass;
import org.junit.BeforeClass;
import org.junit.runner.RunWith;
import org.junit.runners.Suite;

import testUtils.Helpers;
import tests.beans.TestJournalBean;


@RunWith(Suite.class)

@Suite.SuiteClasses({
	TestJournalBean.class
})
public class TestSuite {

    @BeforeClass
    public static void setUp() {
        System.out.println("setting up");
		Helpers.setupDS();

    }

    @AfterClass
    public static void tearDown() {
        System.out.println("tearing down");
        Helpers.clearDS();
    }

}

